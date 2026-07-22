using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlueSapphire.Helpers
{
    public static class MarkdownHelper
    {
        public static readonly DependencyProperty MarkdownProperty =
            DependencyProperty.RegisterAttached(
                "Markdown",
                typeof(string),
                typeof(MarkdownHelper),
                new PropertyMetadata(string.Empty, OnMarkdownChanged));

        public static string GetMarkdown(DependencyObject obj)
        {
            return (string)obj.GetValue(MarkdownProperty);
        }

        public static void SetMarkdown(DependencyObject obj, string value)
        {
            obj.SetValue(MarkdownProperty, value);
        }

        private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RichTextBlock rtb)
            {
                var markdown = e.NewValue as string ?? string.Empty;
                RenderMarkdown(rtb, markdown);
            }
        }

        private static void RenderMarkdown(RichTextBlock rtb, string markdown)
        {
            rtb.Blocks.Clear();
            if (string.IsNullOrWhiteSpace(markdown)) return;

            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
            var document = Markdown.Parse(markdown, pipeline);

            foreach (var block in document)
            {
                rtb.Blocks.Add(ParseBlock(block));
            }
        }

        private static Microsoft.UI.Xaml.Documents.Block ParseBlock(Markdig.Syntax.Block block)
        {
            if (block is ParagraphBlock paragraph)
            {
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
                if (paragraph.Inline != null)
                {
                    foreach (var inline in paragraph.Inline)
                    {
                        p.Inlines.Add(ParseInline(inline));
                    }
                }
                return p;
            }
            else if (block is HeadingBlock heading)
            {
                var p = new Paragraph { Margin = new Thickness(0, 12, 0, 8) };
                if (heading.Inline != null)
                {
                    foreach (var inline in heading.Inline)
                    {
                        p.Inlines.Add(ParseInline(inline));
                    }
                }
                p.FontSize = 24 - (heading.Level * 2);
                p.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                return p;
            }
            else if (block is QuoteBlock quote)
            {
                var p = new Paragraph
                {
                    Margin = new Thickness(12, 0, 0, 12),
                    Foreground = GetBrush("TextMuted")
                };
                foreach (var subBlock in quote)
                {
                    var b = ParseBlock(subBlock);
                    if (b is Paragraph sp)
                    {
                        var inlines = new System.Collections.Generic.List<Microsoft.UI.Xaml.Documents.Inline>();
                        foreach(var inline in sp.Inlines)
                        {
                            inlines.Add(inline);
                        }
                        sp.Inlines.Clear();
                        foreach (var i in inlines) p.Inlines.Add(i);
                    }
                }
                return p;
            }
            else if (block is FencedCodeBlock fencedCode)
            {
                var p = new Paragraph { Margin = new Thickness(0, 8, 0, 12) };
                var codeText = new StringBuilder();
                if (fencedCode.Lines.Lines != null)
                {
                    foreach (var line in fencedCode.Lines.Lines)
                    {
                        if (line.Slice.Text != null)
                            codeText.AppendLine(line.Slice.ToString());
                    }
                }
                
                var border = new Border
                {
                    Background = GetBrush("PanelSurfaceStrong"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 12, 16, 12),
                    BorderBrush = GetBrush("BorderColor"),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock 
                    { 
                        Text = codeText.ToString().TrimEnd('\r', '\n'),
                        FontFamily = new FontFamily("Consolas"), 
                        Foreground = GetBrush("AccentInspect"),
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                
                p.Inlines.Add(new InlineUIContainer { Child = border });
                return p;
            }
            else
            {
                var p = new Paragraph();
                p.Inlines.Add(new Run { Text = block.ToString() });
                return p;
            }
        }

        private static Microsoft.UI.Xaml.Documents.Inline ParseInline(Markdig.Syntax.Inlines.Inline inline)
        {
            if (inline is LiteralInline literal)
            {
                return new Run { Text = literal.Content.ToString() };
            }
            else if (inline is EmphasisInline emphasis)
            {
                var span = new Span();
                if (emphasis.DelimiterCount >= 2) span.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                else span.FontStyle = Windows.UI.Text.FontStyle.Italic;
                foreach (var child in emphasis) span.Inlines.Add(ParseInline(child));
                return span;
            }
            else if (inline is LinkInline link)
            {
                var h = new Hyperlink();
                if (Uri.TryCreate(link.Url, UriKind.Absolute, out Uri? result) &&
                    result.Scheme == Uri.UriSchemeHttps)
                    h.NavigateUri = result;
                foreach (var child in link) h.Inlines.Add(ParseInline(child));
                return h;
            }
            else if (inline is CodeInline code)
            {
                var border = new Border
                {
                    Background = GetBrush("AccentPrimaryBg"),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(2, 0, 2, 0),
                    Child = new TextBlock 
                    { 
                        Text = code.Content, 
                        FontFamily = new FontFamily("Consolas"), 
                        Foreground = GetBrush("TextMain"),
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                return new InlineUIContainer { Child = border };
            }
            else if (inline is ContainerInline container)
            {
                var span = new Span();
                foreach (var child in container) span.Inlines.Add(ParseInline(child));
                return span;
            }
            return new Run { Text = inline.ToString() };
        }

        private static Brush GetBrush(string key)
        {
            return Application.Current.Resources.TryGetValue(key, out object? value) &&
                   value is Brush brush
                ? brush
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }
}
