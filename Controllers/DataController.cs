using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LobbyServer.Db;
using LobbyServerBlazor.Data;
using Messages.FromClient.ToLobbyServer;
using Messages.FromLobbyServer.ToClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TaleWorlds.Core;
using TaleWorlds.Diamond;
using TaleWorlds.Diamond.Rest;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade.Diamond;
using TaleWorlds.PlatformService.Steam;

namespace LobbyServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class DataController : Controller
    {
        public delegate void MessageHandler<in TMessage>(RestDataRequestMessage message, TMessage messageContent,
            ref RestResponse response,
            RestRequestMessage request) where TMessage : Message;

        private readonly ILogger<DataController> _logger;
        private ApiContext _context;
        private Dictionary<Type, Delegate> _messageHandlers = new Dictionary<Type, Delegate>();

        private RestDataJsonConverter _restDataJsonConverter = new RestDataJsonConverter();

        public DataController(ILogger<DataController> logger, ApiContext context)
        {
            _logger = logger;
            _context = context;
            _context.Status = new ServerStatus(false, true, false, false,
                new TextObject("Welcome to MisterOutofTimes Master Server"));
            AddMessageHandler<RequestCustomGameServerListMessage>(HandleRequestCustomGameServerListMessage);
            AddMessageHandler<GetPlayerBadgesMessage>(HandleGetPlayerBadgesMessage);
            AddMessageHandler<ChangeGameTypesMessage>(HandleChangeGameTypesMessage);
            AddMessageHandler<EndHostingCustomGameMessage>(HandleEndHostingCustomGameMessage);
            AddMessageHandler<ChangeRegionMessage>(HandleChangeRegion);
            AddMessageHandler<RequestJoinCustomGameMessage>(HandleRequestJoinCustomGameMessage);
            AddMessageHandler<ResponseCustomGameClientConnectionMessage>(
                HandleResponseCustomGameClientConnectionMessage);
            AddMessageHandler<ClientDisconnectedMessage>(HandleClientDisconnected);
            AddMessageHandler<InitializeSession>(HandleLogin);
            AddMessageHandler<GetAnotherPlayerStateMessage>(HandleGetAnotherPlayerStateMessage);
            AddMessageHandler<RegisterCustomGameMessage>(HandleRegisterCustomGameMessage);
            AddMessageHandler<GetServerStatusMessage>(HandleGetServerStatusMessage);
            AddMessageHandler<UpdateShownBadgeIdMessage>(HandleUpdateShownBadgeIdMessage);
            AddMessageHandler<UpdateCharacterMessage>(HandleUpdateCharacterMessage);
            AddMessageHandler<QuitFromCustomGameMessage>(HandleQuitFromCustomGameMessage);
        }

       

        protected void AddMessageHandler<TMessage>(MessageHandler<TMessage> messageHandler) where TMessage : Message
        {
            _messageHandlers.Add(typeof(TMessage), messageHandler);
        }

        private void HandleUpdateCharacterMessage(RestDataRequestMessage message, UpdateCharacterMessage messagecontent,
            ref RestResponse response, RestRequestMessage request)
        {
            var user = _context.FindUserBySessionCredentials(message.SessionCredentials);
            if (user == default)
                return;
            user.PlayerData.BodyProperties = messagecontent.BodyProperties;

            user.PlayerData.IsFemale =
                messagecontent.IsFemale;

            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private void HandleUpdateShownBadgeIdMessage(RestDataRequestMessage message,
            UpdateShownBadgeIdMessage messagecontent, ref RestResponse response, RestRequestMessage request)
        {
            var user = _context.FindUserBySessionCredentials(message.SessionCredentials);
            if (user == default)
                return;
            user.PlayerData.ShownBadgeId = messagecontent.ShownBadgeId;

            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private void HandleGetServerStatusMessage(RestDataRequestMessage message, GetServerStatusMessage messagecontent,
            ref RestResponse response, RestRequestMessage request)
        {
            var user = _context.FindUserBySessionCredentials(message.SessionCredentials);
            if (user == default)
                return;


            response.EnqueueMessage(new RestDataResponseMessage(new ServerStatusMessage(GetServerStatusForUser(user))));

            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private void HandleMessage(RestDataRequestMessage message, Message messageContent, ref RestResponse response,
            RestRequestMessage request)
        {
            _messageHandlers[messageContent.GetType()].DynamicInvoke(message, messageContent, response, request);
        }
        private void HandleQuitFromCustomGameMessage(RestDataRequestMessage message, QuitFromCustomGameMessage messagecontent, ref RestResponse response, RestRequestMessage request)
        {
            var user = this._context.FindUserBySessionCredentials(message.SessionCredentials);
            if (user != default)
            {
                var hoster = _context.FindServerHosterBy(user.ConnectedServer);
                if (hoster != default)
                {
                    hoster.HostedServer.PlayerCount--;
                }
                
            }
            response.SetSuccessful(true, "ResultFromServerTask");
        }
        [HttpPost("ProcessMessage")]
        public async Task<JsonResult> ProcessMessage()
        {
            RestResponse response = new RestResponse();
            byte[] byteJson = await Request.GetRawBodyBytesAsync();
            string json = Encoding.Unicode.GetString(byteJson);
            RestRequestMessage request =
                JsonConvert.DeserializeObject<RestRequestMessage>(json, _restDataJsonConverter);
            DeleteOldPLayers();
            Console.WriteLine("NEW MESSAGE");
            response.UserCertificate = Guid.NewGuid().ToByteArray();

            User user;
            switch (request)
            {
                case RestDataRequestMessage message:

                    Console.WriteLine($"NEW MESSAGE OF TYPE:${message.MessageType}");

                    HandleMessage(message, message.GetMessage(), ref response, request);


                    if (message.SessionCredentials != null)
                    {
                        response.UserCertificate = message.SessionCredentials.SessionKey.ToByteArray();
                        user = _context.Users.Find(userObj =>
                            userObj.Id.SessionKey == message.SessionCredentials.SessionKey);
                        if (user != null)
                        {
                            user.LastAlive = DateTime.Now;

                            var hasMessage = user.QueuedMessages.TryDequeue(out var result);
                            if (hasMessage)
                            {
                                response.EnqueueMessage(result);
                            }
                        }
                    }

                    break;
                case AliveMessage message:
                    if (message.SessionCredentials != null)
                    {
                        response.UserCertificate = message.SessionCredentials.SessionKey.ToByteArray();
                    }

                    Console.WriteLine($"NEW MESSAGE OF TYPE:${message.GetType().Name}");
                    response.SetSuccessful(true, "Alive");
                    if (message.SessionCredentials != null)
                    {
                        user = _context.Users.Find(userObj =>
                            userObj.Id.SessionKey == message.SessionCredentials.SessionKey);
                        if (user != null)
                        {
                            user.LastAlive = DateTime.Now;

                            var hasMessage = user.QueuedMessages.TryDequeue(out var result);
                            if (hasMessage)
                            {
                                response.EnqueueMessage(result);
                            }
                        }
                    }

                    break;
                case DisconnectMessage message:
                    var usertokick = _context.Users.Find(x => message.UserCertificate == x.Id.SessionKey.ToByteArray());
                    if (usertokick == null)
                    {
                        response.SetSuccessful(true, "Disconnected");
                    }
                    else
                    {
                        _context.Users.Remove(usertokick);
                        user = _context.FindServerHosterBy(usertokick.ConnectedServer);
                        if (user?.HostedServer != null)
                        {
                            user.HostedServer.PlayerCount--;
                        }

                        response.SetSuccessful(true, "Disconnected");
                    }

                    break;
                case ConnectMessage message:
                    Console.WriteLine($"NEW MESSAGE OF TYPE:${message.GetType().Name}");
                    response.SetSuccessful(true, "Alive");
                    break;
                default:
                    Console.WriteLine($"Unhandled Type: ${request.GetType().Name}");
                    break;
            }


            //  response.FunctionResult = new RestObjectFunctionResult();new ServerStatus(new ServerStatus(true,true,true,true,new TextObject("hello world")));
            var settings = new JsonSerializerSettings
            {
                Converters = new JsonConverter[]
                {
                    _restDataJsonConverter
                },
                ContractResolver = new DefaultContractResolver {NamingStrategy = new CamelCaseNamingStrategy()}
            };
            return new JsonResult(response, settings);
        }

        private void DeleteOldPLayers()
        {
            var contextUsers = _context.Users;
            if (contextUsers.Count == 0)
            {
                return;
            }

            contextUsers.Where(u => DateTime.Now.Subtract(u.LastAlive).TotalSeconds > 15).ToList()
                .ForEach(
                    u =>
                    {
                        var hoster = _context.FindServerHosterBy(u.ConnectedServer);
                        if (hoster?.HostedServer != null)
                        {
                            hoster.HostedServer.PlayerCount--;
                        }


                        _context.RemoveUserBySessionCredentials(u.Id);
                    });
        }


        private static void HandleGetAnotherPlayerStateMessage(RestDataRequestMessage restDataRequestMessage,
            GetAnotherPlayerStateMessage messageContent,
            ref RestResponse response, RestRequestMessage request)
        {
            response.FunctionResult =
                new RestDataFunctionResult(new GetAnotherPlayerStateMessageResult(AnotherPlayerState.NotFound, 0));
            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private void HandleEndHostingCustomGameMessage(RestDataRequestMessage message,
            EndHostingCustomGameMessage messageContent,
            ref RestResponse response, RestRequestMessage request)
        {
            var user = _context.FindUserBySessionCredentials(message.SessionCredentials);
            if (user != default) user.HostedServer = null;


            response.FunctionResult =
                new RestDataFunctionResult(new EndHostingCustomGameResult());
            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private void HandleRegisterCustomGameMessage(RestDataRequestMessage restDataRequestMessage,
            RegisterCustomGameMessage messageContent,
            ref RestResponse response, RestRequestMessage request)
        {
            var user = _context.FindUserBySessionCredentials(restDataRequestMessage.SessionCredentials);
            if (user == default)
                return;
            user.HostedServer = new CommunityServerEntry(new GameServerEntry(CustomBattleId.NewGuid(),
                messageContent.ServerName,
                HttpContext.Connection.RemoteIpAddress.ToString(), messageContent.Port, "EU",
                messageContent.GameModule, messageContent.GameType,
                messageContent.Map, 1, messageContent.MaxPlayerCount, true,
                !(messageContent.GamePassword == null || messageContent.GamePassword.IsEmpty())));
            Console.WriteLine(
                $"New Server: {HttpContext.Connection.RemoteIpAddress} {messageContent.Port}");
            user.ConnectedServer = user.HostedServer.entry.Id;
            response.FunctionResult =
                new RestDataFunctionResult(new RegisterCustomGameResult(true));
            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private void HandleLogin(RestDataRequestMessage message, InitializeSession messageContent,
            ref RestResponse response, RestRequestMessage request)
        {
            var session = new SessionCredentials(messageContent.PeerId, SessionKey.NewGuid());

            var playerdata = new PlayerData();
            playerdata.FillWithNewPlayer(messageContent.PlayerId,
                new[] {"FreeForAll", "Captain", "Siege", "Duel", "TeamDeathMatch", "FreeForAll"});
            playerdata.LastPlayerName = messageContent.PlayerName;
            playerdata.LastGameTypes = new[] {"Captain"};

            var user = new User
                {Id = session, QueuedMessages = new Queue<RestResponseMessage>(), PlayerData = playerdata};
            if (messageContent.PlayerId.IsValidSteamId())
            {
                if (_context.SteamIDS.Contains(messageContent.PlayerId.Id2))
                {
                    user.CanHost = true;
                }
            }

            var userStatus = GetServerStatusForUser(user);
            _context.Users.Add(user);
            var initializeSessionResponse = new InitializeSessionResponse(playerdata, userStatus
            );

            response.FunctionResult =
                new RestDataFunctionResult(new LoginResult(session.PeerId, session.SessionKey,
                    initializeSessionResponse));
            response.UserCertificate = session.SessionKey.ToByteArray();
            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private ServerStatus GetServerStatusForUser(User user)
        {
            var userStatus = new ServerStatus(_context.Status.IsMatchmakingEnabled,
                _context.Status.IsCustomBattleEnabled, user.CanHost, _context.Status.IsAntiCheatEnabled,
                new TextObject(user.CanHost
                    ? "SteamID Verified ur allowed to Host"
                    : $"Welcome {user.PlayerData.LastPlayerName} This Project is Unrelated with TaleWorlds ,No Streaming/Recording without allowance, please Report Bugs in the #bug-reports Channel"));
            return userStatus;
        }


        private void HandleClientDisconnected(RestDataRequestMessage message, ClientDisconnectedMessage messageContent,
            ref RestResponse response, RestRequestMessage request)
        {
            _context.RemoveUserBySessionCredentials(message.SessionCredentials);

            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private void HandleResponseCustomGameClientConnectionMessage(RestDataRequestMessage message,
            ResponseCustomGameClientConnectionMessage messageContent, ref RestResponse response,
            RestRequestMessage request)
        {
            var server = _context.Users.Find(x => x.Id.SessionKey == message.SessionCredentials.SessionKey)
                .HostedServer;
            foreach (var joindata in messageContent.PlayerJoinData)
            {
                var users = _context.Users.FindAll(usr =>
                    joindata.PlayerId.ConvertToPeerId() == usr.Id.PeerId && usr.HostedServer == null);
                
                switch (joindata.CustomGameJoinResponse)
                {
                    case CustomGameJoinResponse.Success:
                        users.ForEach(u =>
                        {
                            u.QueuedMessages.Enqueue(new RestDataResponseMessage(
                                JoinCustomGameResultMessage.CreateSuccess(new JoinGameData(
                                    new GameServerProperties(server.entry.ServerName, server.entry.Address,
                                        server.entry.Port, server.Region,
                                        server.entry.GameModule, server.entry.GameType, server.entry.Map, "", "",
                                        server.entry.MaxPlayerCount,
                                        server.entry.IsOfficial), joindata.PeerIndex, joindata.SessionKey))));
                            server.PlayerCount++;
                        });

                        break;
                    default:
                        users.ForEach(u =>
                            u.QueuedMessages.Enqueue(
                                new RestDataResponseMessage(
                                    JoinCustomGameResultMessage.CreateFailed(joindata.CustomGameJoinResponse))));

                        break;
                }
            }

            response.SetSuccessful(true, "ResultFromServerTask");
        }

        // @TODO:  ClientWantsToConnectCustomGameMessage
        private void HandleRequestJoinCustomGameMessage(RestDataRequestMessage message,
            RequestJoinCustomGameMessage messageContent,
            ref RestResponse response, RestRequestMessage request)
        {
            User ServerOwner = _context.FindServerHosterBy(messageContent.CustomBattleId);
            
            User JoiningUser = _context.FindUserBySessionCredentials(message.SessionCredentials);
            if (ServerOwner == default || JoiningUser == default)
            {
                return;
            }
            if (ServerOwner.HostedServer == null) return;


            /*@TODO:
                //var result =  JoinCustomGameResultMessage.CreateSuccess(new JoinGameData(properties,0,0 ));
                ServerOwner.QueuedMessages.Add(new ClientWantsToConnectCustomGameMessage(new PlayerJoinGameData[]{new PlayerJoinGameData(JoiningUser.PlayerData,JoiningUser.PlayerData.LastPlayerName) },msg.Password ));
                
                response.EnqueueMessage(new RestDataResponseMessage(result));
                */
            ServerOwner.QueuedMessages.Enqueue(new RestDataResponseMessage(
                new ClientWantsToConnectCustomGameMessage(
                    new[]
                        {new PlayerJoinGameData(JoiningUser.PlayerData, JoiningUser.PlayerData.LastPlayerName,null)},
                    messageContent.Password)));

            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private void HandleRequestCustomGameServerListMessage(RestDataRequestMessage message,
            RequestCustomGameServerListMessage messageContent, ref RestResponse response, RestRequestMessage request)
        {
            var serverList = new AvailableCustomGames();
            foreach (var server in _context.Users.Select(u => u.HostedServer))
            {
                if (server != null)
                    serverList.CustomGameServerInfos
                        .Add(new GameServerEntry(server.entry.Id, server.entry.ServerName,
                            server.entry.Address, server.entry.Port, server.entry.Region, server.entry.GameModule,
                            server.entry.GameType, server.entry.Map
                            , server.PlayerCount, server.entry.MaxPlayerCount, server.entry.IsOfficial,
                            server.entry
                                .PasswordProtected)); // serverlist.CustomGameServerInfos.Add(new GameServerEntry());
            }

            var resp = new CustomGameServerListResponse(serverList);
            response.FunctionResult = new RestDataFunctionResult(resp);
            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private void HandleGetPlayerBadgesMessage(RestDataRequestMessage message, GetPlayerBadgesMessage messageContent,
            ref RestResponse response, RestRequestMessage request)
        {
            var user = _context.FindUserBySessionCredentials(message.SessionCredentials);
            if (user == default)
                return;
            response.FunctionResult =
                new RestDataFunctionResult(new GetPlayerBadgesMessageResult(new[]
                    {user.CanHost ? "badge_taleworlds_primary_dev" : ""}));
            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private static void HandleChangeGameTypesMessage(RestDataRequestMessage message, ChangeGameTypesMessage messageContent,
            ref RestResponse response, RestRequestMessage request)
        {
            response.SetSuccessful(true, "ResultFromServerTask");
        }

        private static void HandleChangeRegion(RestDataRequestMessage restDataRequestMessage,
            ChangeRegionMessage messageContent, ref RestResponse response, RestRequestMessage request)
        {
            response.SetSuccessful(true, "ResultFromServerTask");
        }
    }
}