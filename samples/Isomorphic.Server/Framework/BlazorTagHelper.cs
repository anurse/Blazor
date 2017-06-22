using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Isomorphic.Server.Framework
{
    [HtmlTargetElement("blazor-app")]
    public class BlazorTagHelper : TagHelper
    {
        public string Component { get; set; }

        public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            output.TagName = null;
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Content.SetHtmlContent($"<h1>Blazing a trail, root component: {Component}</h1>");
            return Task.CompletedTask;
        }
    }
}
