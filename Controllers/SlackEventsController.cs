using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.CognitiveServices.Search.ImageSearch;
using Microsoft.Azure.CognitiveServices.Search.ImageSearch.Models;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Net;
using Newtonsoft.Json;
using System.Text;
using Hangfire;
using McMaster.Extensions.CommandLineUtils;

namespace imajin.Controllers
{
    [Route("slackEvents")]
    [ApiController]
    public class SlackEventsController : ControllerBase
    {

        private readonly ILogger logger;

        //FIXME
        private readonly HashSet<string> enqueuedPosts = new HashSet<string>();

        public SlackEventsController(ILoggerFactory loggerFactory) {
            logger = loggerFactory.CreateLogger<SlackEventsController>();
        }

        [HttpPost]
        public async Task<ActionResult> Incoming() {
            string body;
            using (var stream = new StreamReader(HttpContext.Request.Body)) {
                body = stream.ReadToEnd();
            }

            logger.LogInformation(body);

            var req = JObject.Parse(body);

            switch ( (string)req["type"] ) {
                case "url_verification":
                    logger.LogInformation("url_verification");
                    return DoChallenge(body);
                case "event_callback":
                    logger.LogInformation("event_callback");
                    return await DoEvent(body);
            }

            logger.LogInformation("mismatched.");
            throw new InvalidOperationException("Type is not matched.");
        }

        private JsonResult DoChallenge(string body) {
            var req = JObject.Parse(body);
            
            var res = new ChallengeResponse() {
                challenge = (string)req["challenge"]
            };

            return new JsonResult(res);
        }

        private async Task<ActionResult> DoEvent(string body) {
            var req = JObject.Parse(body);

            switch ( (string) req["event"]["type"] ) {
                case "app_mention":
                    return await DoMention(
                        (string)req["event"]["ts"],
                        body
                    );
            }

            throw new InvalidOperationException();
        }

        private async Task<ActionResult> DoMention(string postId, string body) {
            lock (enqueuedPosts) {
                if (!enqueuedPosts.Contains(postId)) {
                    BackgroundJob.Enqueue(() => DoMention_Job(body));
                    enqueuedPosts.Add(postId);
                }
            }
            return new OkResult();
        }

        public static async Task DoMention_Job(string body) {
            var req = JObject.Parse(body);

            var mentionedBy = (string) req["event"]["user"];
            var text = (string) req["event"]["text"];
            var channel = (string) req["event"]["channel"];

            var splitted = text.Split(new char[] { ' ', '　' }).ToArray();

            if (splitted.Length < 1 || splitted[0].IndexOf("<@") != 0) {
                return; // ignore
            }

            var args = splitted.Skip(1).ToArray();

            var parser = new CommandLineApplication();
            var limit = parser.Option<int>("--limit <LIMITATION>", "Limitation", CommandOptionType.SingleValue);
            var terms = parser.Argument<string>("Terms", "Search terms", multipleValues: true);

            parser.OnExecute(async () => {
                var limitation = limit.HasValue() ? limit.ParsedValue : 3;
                var client = new ImageSearchAPI(new ApiKeyServiceClientCredentials(Environment.GetEnvironmentVariable("IMAJIN_BING_KEY")));

                var result = await client.Images.SearchAsync(
                    string.Join(" ", terms.Values),
                    count: limitation,
                    license: "All",
                    safeSearch: "Strict"
                );

                if (result.Value.Count < 1) {
                    await PostImageToSlack(channel, null);
                } else {
                    await PostImageToSlack(channel, result.Value.Take(3).OrderBy(_ => Guid.NewGuid()).First().ThumbnailUrl);
                }
            });

            parser.OnValidationError((e) => {
                throw new ArgumentException(e.ToString());
            });

            parser.Execute(args);
        }

        private static async Task PostImageToSlack(string channel, string thumbnailUrl)
        {
            using (var client = new HttpClient()) {
                object payload;

                if (thumbnailUrl != null) {
                    payload = new ImagePost() {
                        token = getToken(),
                        channel = channel,
                        text = "",
                        attachments = new List<ImagePostAttachment>() {
                            new ImagePostAttachment() {
                                fallback = thumbnailUrl,
                                image_url = thumbnailUrl
                            }
                        }
                    };
                } else {
                    payload = new TextPost() {
                        token = getToken(),
                        channel = channel,
                        text = "そんな画像はありません"
                    };
                }

                var msg = new HttpRequestMessage {
                    RequestUri = new Uri("https://slack.com/api/chat.postMessage"),
                    Method = HttpMethod.Post,
                    Headers = {
                        { HttpRequestHeader.Authorization.ToString(), $"Bearer {getToken()}" },
                        { HttpRequestHeader.ContentType.ToString(), "application/json" },
                    },
                    Content = new StringContent(
                        JsonConvert.SerializeObject(payload),
                        Encoding.UTF8,
                        "application/json"
                    )
                };

                var reslut = await client.SendAsync(msg);

                await reslut.Content.ReadAsStringAsync();
            }
        }

        private static string getToken() {
            return Environment.GetEnvironmentVariable("IMAJIN_SLACK_TOKEN");
        }

    }

}
