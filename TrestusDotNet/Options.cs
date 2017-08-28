namespace TrestusDotNet
{
    internal class Options
    {
        public string Key { get; set; }
        public string Secret { get; set; }
        public string Token { get; set; }
        public string TokenSecret { get; set; }
        public string BoardId { get; set; }
        public string CustomTemplate { get; set; }
        public string TemplateData { get; set; }
        public bool SkipCss { get; set; }
        public string OutputPath { get; set; }
    }
}