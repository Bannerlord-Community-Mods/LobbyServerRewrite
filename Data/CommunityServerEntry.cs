using TaleWorlds.MountAndBlade.Diamond;

namespace LobbyServerBlazor.Data
{
    public class CommunityServerEntry 
    {    
        public GameServerEntry entry { get;  set; }


        public string Region { get;  set; }

        public int PlayerCount { get;  set; }

        public CommunityServerEntry(GameServerEntry entry)
        {
            this.entry = entry;
            Region = entry.Region;
            PlayerCount = entry.PlayerCount;
        }
        
    }
}