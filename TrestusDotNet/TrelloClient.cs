using Manatee.Trello;
using Manatee.Trello.ManateeJson;
using Manatee.Trello.WebApi;

namespace TrestusDotNet
{
    internal class TrelloClient
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string Token { get; set; }
        public string TokenSecret { get; set; }

        public Board GetBoard(string boardId)
        {
            var serializer = new ManateeSerializer();
            TrelloConfiguration.Serializer = serializer;
            TrelloConfiguration.Deserializer = serializer;
            TrelloConfiguration.JsonFactory = new ManateeFactory();
            TrelloConfiguration.RestClientProvider = new WebApiClientProvider();
            TrelloAuthorization.Default.AppKey = ApiKey;
            TrelloAuthorization.Default.UserToken = TokenSecret;
            return new Board(boardId);
        }
    }
}