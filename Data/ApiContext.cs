using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Diamond;
using TaleWorlds.MountAndBlade.Diamond;

namespace LobbyServer.Db
{
    public class ApiContext
    {
        public ApiContext()
        {
            Load();
        }


        public List<User> Users { get; set; } = new List<User>();
        public ServerStatus Status;
        public List<ulong> SteamIDS = new List<ulong>();

        public User FindServerHosterBy(CustomBattleId battleid)
        {
            return Users.FirstOrDefault(x => x.HostedServer != null && x.HostedServer.entry.Id == battleid);
        }

        public User FindUserBySessionCredentials(SessionCredentials credentials)
        {
            return Users.FirstOrDefault(x => x.Id.SessionKey == credentials.SessionKey);
        }

        public bool RemoveUserBySessionCredentials(SessionCredentials credentials)
        {
            User findUserBySessionCredentials = this.FindUserBySessionCredentials(credentials);
            return findUserBySessionCredentials != default && Users.Remove(findUserBySessionCredentials);
        }

        public void Save()
        {
            System.IO.File.WriteAllLines(steamidsTxt,
                SteamIDS.Select(tb => (tb).ToString()));
        }

        const string steamidsTxt = "steamids.txt";
        private void Load()
        {
            if (!System.IO.File.Exists(steamidsTxt))
                System.IO.File.Create(steamidsTxt).Close();
            this.SteamIDS = System.IO.File.ReadAllLines(steamidsTxt).Select(ulong.Parse).ToList();
        }
    }
}