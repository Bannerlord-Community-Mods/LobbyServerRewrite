using System;
using System.Collections.Generic;
using LobbyServerBlazor.Data;
using TaleWorlds.Diamond;
using TaleWorlds.Diamond.Rest;
using TaleWorlds.MountAndBlade.Diamond;

namespace LobbyServer.Db
{
    public class User
    {
        public SessionCredentials Id { get; set; }
 
        public Queue<RestResponseMessage> QueuedMessages { get; set; }
        public PlayerData PlayerData { get; set; }

        public CommunityServerEntry HostedServer;
        public bool CanHost;
        public DateTime LastAlive = DateTime.Now;
        public CustomBattleId ConnectedServer;
    }
}