namespace AwAVR.AnimationPropertyFinder
{
    internal class Result
    {
        public string assetPath;
        public string animationClipName;
        public string bindingPath;
        public string propertyName;

        public Result(string assetPath, string animationClipName, string bindingPath, string propertyName)
        {
            this.assetPath = assetPath;
            this.animationClipName = animationClipName;
            this.bindingPath = bindingPath;
            this.propertyName = propertyName;
        }
    }
}