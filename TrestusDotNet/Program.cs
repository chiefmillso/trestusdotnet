using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using Manatee.Trello;
using NDesk.Options.Fork;
using NDesk.Options.Fork.Common;

namespace TrestusDotNet
{
    class Program
    {
        const string StatusPrefix = "status:";
            
        public enum Severity
        {
            None,
            DegradedPerformance,
            MinorOutage,
            MajorOutage
        }

        public class SystemDto
        {
            public string Status { get; set; }
            public Severity Severity { get; set; }
        }

        public class CommentDto
        {
            public string HtmlDescription { get; set; }
            public DateTime? ParsedDate { get; set; }

            public MemberDto MemberCreator { get; set; }
        }

        public class MemberDto
        {
            public string Initials { get; set; }
        }

        public class LabelDto
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public LabelColor? Color { get; set;}
        }

        public class CardDto
        {
            public Severity Severity { get; set; }
            public bool Closed { get; set; }
            public string HtmlDescription { get; set; }
            public List<CommentDto> ParsedComments { get; set; }
        }

        static void Main(string[] args)
        {
            var options = new Options()
            {
                Key = Environment.GetEnvironmentVariable("TRELLO_KEY", EnvironmentVariableTarget.Machine),
                Secret = Environment.GetEnvironmentVariable("TRELLO_SECRET", EnvironmentVariableTarget.Machine),
                Token = Environment.GetEnvironmentVariable("TRELLO_TOKEN", EnvironmentVariableTarget.Machine),
                TokenSecret = Environment.GetEnvironmentVariable("TRELLO_TOKEN", EnvironmentVariableTarget.Machine),
                BoardId = Environment.GetEnvironmentVariable("TRELLO_BOARD_ID", EnvironmentVariableTarget.Machine)
            };

            bool showHelp = false;
            var p = new OptionSet()
            {
                {"k|key=", "Trello API key", v => options.Key = v},
                {"s|secret=", "Trello API secret", v => options.Secret = v},
                {"t|token=", "Trello API auth token", v => options.Token = v},
                {"S|token-secret=", "Trello API auth token secret", v => options.TokenSecret = v},
                {"b|board-id=", "Trello board id", v => options.BoardId = v},
                {"T|custom-template=", "Custom handlebars template to use instead of default", v => options.CustomTemplate = v},
                {"d|template-data=", "If using --custom-template, you can provide a YAML file to load in data that would be available in the template the template", v => options.TemplateData = v},
                {"skip-css", "Skip copying the default trestus.css to the output dir.", v => options.SkipCss = v != null},
                {"output-path=", "Path to output rendered HTML to", v => options.OutputPath = v},
                {"h|help", "Show Help", v => showHelp = v != null }
            };

            try
            {
                p.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("trestusdotnet: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `trestusdotnet --help' for more information.");
                return;
            }

            if (showHelp)
            {
                p.WriteOptionDescriptions(Console.Out);
                return;
            }

            var client = new TrelloClient()
            {
                ApiKey = options.Key,
                ApiSecret = options.Secret,
                Token = options.Token,
                TokenSecret = options.TokenSecret
            };

            var markdown = new HeyRed.MarkdownSharp.Markdown();

            var board = client.GetBoard(options.BoardId);
            var labels = board.Labels.Select(x => new LabelDto() { Color = x.Color, Id = x.Id, Name = x.Name}).ToList();
            var serviceLabels = labels.Where(x => !x.Name.StartsWith(StatusPrefix)).ToArray();
            var serviceIds = serviceLabels.Select(x => x.Id).ToArray();
            var statusTypes = labels.Where(x => !serviceLabels.Contains(x)).ToArray();
            var lists = board.Lists;

            var incidents = new List<CardDto>();
            var panels = new Dictionary<Severity, List<List<string>>>();
            var systems = new Dictionary<string, SystemDto>();

            foreach (var cardList in lists)
            {
                var cards = cardList.Cards.OrderByDescending(x => x.CreationDate).ToList();
                foreach (var card in cards)
                {
                    CardDto cardDto = new CardDto();
                    Severity severity = Severity.None;
                    foreach (var label in card.Labels)
                    {
                        var labelName = label.Name;
                        if (!labelName.StartsWith(StatusPrefix))
                            continue;

                        var suffix = labelName.Substring(StatusPrefix.Length).Replace(" ", "");
                        if (!Enum.TryParse(suffix, true, out severity))
                            break;

                        if (label.Color == LabelColor.Red)
                            break;
                    }
                    cardDto.Severity = severity;

                    var cardServiceLabels = card.Labels.Where(x => serviceIds.Contains(x.Id)).Select(x => x.Name).ToList();
                    if (cardServiceLabels.Count == 0 || severity == Severity.None)
                        continue;

                    if (cardList.Name == "fixed")
                    {
                        cardDto.Closed = true;
                    }
                    else
                    {
                        if (!panels.ContainsKey(cardDto.Severity))
                            panels[severity] = new List<List<string>>();
                        panels[severity].Add(cardServiceLabels);

                        foreach (var service in cardServiceLabels)
                        {
                            if (systems.ContainsKey(service))
                                continue;

                            systems[service] = new SystemDto()
                            {
                                Status = cardList.Name,
                                Severity = severity
                            };
                        }
                    }

                    cardDto.HtmlDescription = markdown.Transform(card.Description ?? "");
                    cardDto.ParsedComments = new List<CommentDto>();

                    var comments = card.Comments;
                    foreach (var comment in comments)
                    {
                        var commentDto = new CommentDto
                        {
                            ParsedDate = comment.Date,
                            HtmlDescription = markdown.Transform(comment.Data.Text),
                            MemberCreator = new MemberDto()
                            {
                                Initials = comment.Creator.Initials
                            }
                        };
                        cardDto.ParsedComments.Add(commentDto);
                    }

                    incidents.Add(cardDto);
                }
            }

            foreach (var label in serviceLabels)
            {
                if (systems.ContainsKey(label.Name))
                    continue;

                systems[label.Name] = new SystemDto()
                {
                    Status = "Operational",
                    Severity = Severity.None
                };
            }
            

            dynamic templateData = new ExpandoObject();
            templateData.incidents = incidents;
            templateData.panels = panels;
            templateData.systems = systems;

            
            // TODO: process alternate templates
            Func<object, string> render;
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("TrestusDotNet.Templates.trestus.html"))
            {
                if (stream == null)
                    throw new InvalidOperationException("Could not load embedded stream for the html template");

                using (var reader = new StreamReader(stream))
                {
                    var template = reader.ReadToEnd();
                    render = HandlebarsDotNet.Handlebars.Compile(template);
                }
            }

            var html = render(templateData);

            Console.WriteLine(html);
        }
    }
}
