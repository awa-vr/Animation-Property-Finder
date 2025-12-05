using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AwAVR.AnimationPropertyFinder
{
    public class AnimationPropertyFinder : EditorWindow
    {
        #region Variables

        private static readonly string WindowTitle = "Animation Property Finder";

        // UXML Asset Reference
        private VisualTreeAsset _visualTree;

        // UI Elements references
        private ObjectField _objectField;
        private TextField _propertyTextField;
        private DropdownField _propertyPresetDropdown; // NEW
        private MultiColumnListView _listView;
        private Label _statusLabel;

        // Data
        private Transform _transform;
        private string _objectName = "";
        private string _propertyName = "";
        private readonly List<Result> _results = new List<Result>();

        private static readonly List<string> PropertyPresets = new List<string>
        {
            "None",
            "_UDIMDiscardRow0_*",
            "_UDIMDiscardRow*",
            "*" // Wildcard
        };

        // Find the UXML path
        private static readonly string UxmlPath =
            "Packages/com.awavr.animation-property-finder/Editor/AnimationPropertyFinderUI.uxml";

        #endregion

        #region UI

        [MenuItem("Tools/AwA/Animation Property Finder", false, -100)]
        public static void ShowWindow()
        {
            var window = GetWindow<AnimationPropertyFinder>(WindowTitle);
            window.titleContent = new GUIContent(
                image: EditorGUIUtility.IconContent("d_AnimationClip Icon").image,
                text: WindowTitle,
                tooltip: "Find all animations where a property is animated"
            );
            window.minSize = new Vector2(500f, 400f);
        }

        public void CreateGUI()
        {
            // Load UXML structure
            _visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (_visualTree == null)
            {
                Debug.LogError($"Could not load UXML from path: {UxmlPath}. Make sure the file exists!");
                return;
            }

            VisualElement root = rootVisualElement;
            _visualTree.CloneTree(root);

            // Get References to UI Elements
            _objectField = root.Q<ObjectField>("object-field");
            _propertyTextField = root.Q<TextField>("property-text-field");
            _propertyPresetDropdown = root.Q<DropdownField>("property-preset-dropdown"); // NEW REFERENCE
            _listView = root.Q<MultiColumnListView>("results-list-view");
            _statusLabel = root.Q<Label>("status-label");
            var searchButton = root.Q<Button>("search-button");
            var imguiHeaderContainer = root.Q<IMGUIContainer>("imgui-header-container");

            // Setup Header
            if (imguiHeaderContainer != null)
            {
                imguiHeaderContainer.onGUIHandler = () => { Core.Title(WindowTitle); };
            }

            // Initialize Data and Register Callbacks
            // Object Field Setup
            _objectField.objectType = typeof(Transform);
            _objectField.value = _transform;
            _objectField.RegisterValueChangedCallback(evt =>
            {
                _transform = (Transform)evt.newValue;
                if (_transform != null)
                {
                    _objectName = _transform.name;
                }
            });

            // Property Name Field Setup
            _propertyTextField.value = _propertyName;
            _propertyTextField.RegisterValueChangedCallback(evt => _propertyName = evt.newValue);

            // Property Preset Dropdown Setup
            _propertyPresetDropdown.choices = PropertyPresets;
            _propertyPresetDropdown.value = PropertyPresets[0]; // Default to "None"
            _propertyPresetDropdown.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != PropertyPresets[0])
                {
                    // If a preset other than "None" is selected, update the property text field
                    _propertyTextField.value = evt.newValue;
                    _propertyName = evt.newValue; // Also update the backing field
                }
            });


            // Search Button
            searchButton.clicked += PerformSearch;

            // Setup MultiColumnListView
            SetupListView();
        }

        private void SetupListView()
        {
            _listView.itemsSource = _results;

            // Define Columns
            var colClip = new Column { title = "Animation Clip", width = 200, stretchable = true, sortable = true };
            colClip.makeCell = () => new Label();
            colClip.bindCell = (element, index) => { ((Label)element).text = _results[index].animationClipName; };

            var colPath = new Column { title = "Path", width = 50, stretchable = true };
            colPath.makeCell = () => new Label();
            colPath.bindCell = (element, index) => { ((Label)element).text = _results[index].bindingPath; };

            var colProp = new Column { title = "Property", width = 150, stretchable = true };
            colProp.makeCell = () => new Label();
            colProp.bindCell = (element, index) => { ((Label)element).text = _results[index].propertyName; };

            _listView.columns.Add(colClip);
            _listView.columns.Add(colPath);
            _listView.columns.Add(colProp);

            // Selection Logic
            _listView.selectionChanged += (selectedItems) =>
            {
                var selectedResult = selectedItems.FirstOrDefault() as Result;
                if (selectedResult != null && !string.IsNullOrEmpty(selectedResult.assetPath))
                {
                    var clipAsset = AssetDatabase.LoadAssetAtPath<AnimationClip>(selectedResult.assetPath);
                    if (clipAsset != null)
                    {
                        Selection.activeObject = clipAsset;
                        EditorGUIUtility.PingObject(clipAsset);
                    }
                }
            };
        }

        #endregion

        #region Logic

        private void PerformSearch()
        {
            if (_objectField.value != null) _objectName = ((Transform)_objectField.value).name;
            _propertyName = _propertyTextField.value;

            FindPropertyInAnimations(_objectName, _propertyName);
            _listView.RefreshItems();
            _statusLabel.text = $"Results: {_results.Count}";
        }

        private void FindPropertyInAnimations(string mesh, string property)
        {
            _results.Clear();

            if (string.IsNullOrEmpty(mesh) && _transform != null) mesh = _transform.name;

            if (string.IsNullOrEmpty(mesh) || string.IsNullOrEmpty(property))
            {
                Debug.LogWarning("Please enter both Object Name and Property names.");
                _statusLabel.text = "Error: Missing Input";
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
            int totalFiles = guids.Length;
            int matchCount = 0;

            Regex cachedRegex = null;
            bool useRegex = property.Contains("*");
            if (useRegex)
            {
                string regexPattern = Regex.Escape(property).Replace("\\*", ".*");
                cachedRegex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }

            try
            {
                for (int i = 0; i < totalFiles; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Searching Animations",
                            $"Scanning {i}/{totalFiles}: ...",
                            (float)i / totalFiles))
                    {
                        Debug.Log("Search cancelled by user.");
                        break;
                    }

                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                    if (clip == null) continue;

                    var bindings = AnimationUtility.GetCurveBindings(clip);
                    bool clipHasMatch = false;

                    foreach (var binding in bindings)
                    {
                        if (!binding.path.Contains(mesh)) continue;

                        bool isMatch = false;
                        if (useRegex)
                        {
                            isMatch = cachedRegex.IsMatch(binding.propertyName);
                        }
                        else
                        {
                            isMatch = binding.propertyName.IndexOf(
                                property,
                                System.StringComparison.OrdinalIgnoreCase) >= 0;
                        }

                        if (isMatch)
                        {
                            var result = new Result(path, clip.name, binding.path, binding.propertyName);
                            _results.Add(result);
                            matchCount++;
                            clipHasMatch = true;
                        }
                    }

                    if (!clipHasMatch)
                    {
                        Resources.UnloadAsset(clip);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"Search complete. Found {matchCount} matches.");
        }

        #endregion
    }
}