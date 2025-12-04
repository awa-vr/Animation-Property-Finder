using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AwAVR;

public class AnimationPropertyFinder : EditorWindow
{
    private static readonly string WindowTitle = "Animation Property Finder";
    private Transform _transform;
    private string _objectName = "";
    private string _propertyName = "";
    private Vector2 _scroll;

    private readonly List<string> _results = new List<string>();

    [MenuItem("Tools/AwA/Animation Property Finder", false, -100)]
    public static void ShowWindow()
    {
        var window = GetWindow<AnimationPropertyFinder>(WindowTitle);
        window.titleContent = new GUIContent(
            image: EditorGUIUtility.IconContent("d_AnimationClip Icon").image,
            text: WindowTitle,
            tooltip: "Find all animations where a property is animated"
        );
        window.minSize = new Vector2(450f, window.minSize.y);
    }

    private void OnGUI()
    {
        Core.Title(WindowTitle);

        _transform = (Transform)EditorGUILayout.ObjectField(
            new GUIContent("Object", "Drag in an object to automatically set the name"),
            _transform,
            typeof(Transform),
            allowSceneObjects: true);

        // Auto-update mesh name if transform changes
        if (_transform != null && string.IsNullOrEmpty(_objectName))
        {
            _objectName = _transform.name;
        }

        // _objectName = EditorGUILayout.TextField(new GUIContent("Object Name", "Animated object name (can be automatically set by Object field)"), _objectName);
        _propertyName =
            EditorGUILayout.TextField(new GUIContent("Property Name", "Use '*' as a wildcard"), _propertyName);

        if (GUILayout.Button("Search"))
        {
            FindPropertyInAnimations(_objectName, _propertyName);
        }

        GUILayout.Space(10);
        GUILayout.Label($"Results ({_results.Count}):", EditorStyles.boldLabel);

        _scroll = GUILayout.BeginScrollView(_scroll);
        if (_results.Count == 0)
        {
            GUILayout.Label("No matches found.");
        }
        else
        {
            foreach (string result in _results)
            {
                // Selectable label so you can copy-paste results
                EditorGUILayout.SelectableLabel(result, GUILayout.Height(18));
            }
        }

        GUILayout.EndScrollView();
    }

    private void FindPropertyInAnimations(string mesh, string property)
    {
        _results.Clear();

        if (string.IsNullOrEmpty(mesh) || string.IsNullOrEmpty(property))
        {
            Debug.LogWarning("Please enter both Mesh and Property names.");
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
            // Compiled option improves performance for repeated checks
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
                    // Check Path first (cheaper string comparison)
                    if (!binding.path.Contains(mesh)) continue;

                    // Check Property Name
                    bool isMatch = false;
                    if (useRegex)
                    {
                        isMatch = cachedRegex.IsMatch(binding.propertyName);
                    }
                    else
                    {
                        // Case insensitive contains is usually preferred for user search tools
                        isMatch = binding.propertyName.IndexOf(
                            property,
                            System.StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    if (isMatch)
                    {
                        string result = $"{clip.name} → {binding.path} → {binding.propertyName}";
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

        Debug.Log($"Search complete. Found {matchCount} matches in {_results.Count} lines.");
    }
}