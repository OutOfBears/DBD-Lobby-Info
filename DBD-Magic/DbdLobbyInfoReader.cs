using DBD_Magic.Responses;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace DBD_Magic
{
    internal struct RegexCallback
    {
        public delegate void Callback(Match result);
        public Callback callback;
        public Regex regex;

    };

    public class CharacterCustomizationArgs
    {
        public string  Outfit { get; }
        public CustomizationResponse Item { get; }
        public CharacterInfoResponse Character { get; }

        public CharacterCustomizationArgs(string outfit, CustomizationResponse item, CharacterInfoResponse character)
        {
            Item = item;
            Outfit = outfit;
            Character = character;
        }
    }

    public class MatchInfoArgs
    {
        public MatchResponse Match { get; }

        public MatchInfoArgs(MatchResponse match)
        {
            Match = match;
        }
    }

    public class DbdLobbyInfoReader
    {
        private const bool DEBUG_MESSAGES = false;

        // Just some public stuff
        public string DBDBaseDirectory { get; private set; }

        // Events
        public event EventHandler<CharacterCustomizationArgs> LobbyCharacterCustomizationChanged;
        public event EventHandler<MatchInfoArgs> OnMatchInfo;
        public event EventHandler<FriendsResponse> OnKillerInfo;
        public event EventHandler OnLobbyLeave;
        

        // Private Variables
        private static Mutex mutex = new Mutex();
        private static readonly uint[] _rankSet = {
            85, 80, 75, 70, 65, 60, 55, 50,
            45, 40, 35, 30, 26, 22, 18, 14,
            10,  6,  3,  0
        };

        private ApiClient _httpClient;
        private List<RegexCallback> _callbacks;
        private Dictionary<string, CharacterInfoResponse> _characters;
        private Dictionary<string, CustomizationResponse> _customization;
        private FileSystemWatcher _watcher;
        private bool _readCustoms;
        private int _lastLine;

        private async Task GetCharacters()
        {
            var response = await _httpClient.GetAsync("https://dbd-stats.info/api/characters");
            if (!response.IsSuccessStatusCode)
                throw new Exception("Failed to get DBD Character info");

            var info = CharacterInfoResponse.FromJson(await response.Content.ReadAsStringAsync());
            if (info == null || info.Equals(default))
                throw new Exception("Failed to convert to json");

            _characters = info;
        }

        private async Task GetCustomizationItems()
        {
            var response = await _httpClient.GetAsync("https://dbd-stats.info/api/customizationitems");
            if (!response.IsSuccessStatusCode)
                throw new Exception("Failed to get DBD store outfits");

            var customization = CustomizationResponse.FromJson(await response.Content.ReadAsStringAsync());
            if (customization == null || customization.Equals(default))
                throw new Exception("Failed to convert to json");

            _customization = customization;
        }

        private void OnSetStartingMap(Match match)
        {
            var url = match.Groups[1].Value;
            if (!url.Contains("//") && url.StartsWith("/"))
                url = $"localhost{url}";

            url = url.Replace("?", "&")
                .Replace("OnlineLobby&", "OnlineLobby?");

            var uriBuilder = new UriBuilder(url);
            var queries = new NameValueCollection();
            if (!string.IsNullOrEmpty(uriBuilder.Query))
                queries = HttpUtility.ParseQueryString(uriBuilder.Query);

            switch (uriBuilder.Path)
            {
                case "//Game/Maps/OnlineLobby":
                    break;

                case "/Game/Maps/OfflineLobby":
                    OnLobbyLeave?.Invoke(this, new EventArgs());
                    break;

                default:
                    break;
            }

        }

        private void OnCustomizationSelect(Match match)
        {
            try
            {
                mutex.WaitOne();

                var outfit = match.Groups[1].Value;
                var character = _customization.TryGetValue(outfit, out var item) ? 
                    _characters[item.AssociatedCharacter.ToString()] : null;

                if (character == null || character.Equals(default))
                    return;

                LobbyCharacterCustomizationChanged?.Invoke(this, new CharacterCustomizationArgs(outfit, item, character));
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        // such a hacky way
        private async Task<FriendsResponse> GetCloudIdInfo(string cloudId)
        {
            var friends = await GetFriends(cloudId);
            if(friends != null && friends.Length > 0)
            {
                var friend = friends.FirstOrDefault(x => x.Status == Status.Confirmed);
                if (friend != null && !friend.Equals(default)) {
                    var friendFriends = await GetFriends(friend.UserId.ToString());
                    if (friendFriends != null && friendFriends.Length > 0)
                    {
                        var user = friendFriends.FirstOrDefault(x => x.UserId.ToString() == cloudId);
                        if (user != null && !user.Equals(default))
                        {
                            return user;
                        }
                    }
                }
            }

            return null;
        }

        private async Task<FriendsResponse[]> GetFriends(string cloudId)
        {
            if (string.IsNullOrEmpty(cloudId))
                return null;

            var response = await _httpClient.GetAsync($"https://steam.live.bhvrdbd.com/api/v1/players/{cloudId}/friends");
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                return null;

            return FriendsResponse.FromJson(await response.Content.ReadAsStringAsync());
        }

        private async void OnMatchRequest(Match match)
        {
            var url = "https://steam.live.bhvrdbd.com/api/v1/match/" + match.Groups[1].Value;
            var response = await _httpClient.GetAsync(url);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                return;

            var matchResponse = MatchResponse.FromJson(await response.Content.ReadAsStringAsync());
            if (matchResponse == null || matchResponse.Equals(default))
                return;

            OnMatchInfo?.Invoke(this, new MatchInfoArgs(matchResponse));

            if (matchResponse.SideA != null && matchResponse.SideA.Length > 0)
            {
                var userInfo = await GetCloudIdInfo(matchResponse.SideA[0].ToString());
                if(userInfo != null && !userInfo.Equals(default))
                {
                    OnKillerInfo?.Invoke(this, userInfo);
                }
            }
        }

        private void ExecuteCallbackFromLine(string line)
        {
            if (DEBUG_MESSAGES) Console.WriteLine("{0}", line);

            foreach(var callbackTest in _callbacks)
            {
                try
                {
                    var matches = callbackTest.regex.Matches(line);
                    if (matches.Count > 0)
                    {
                        callbackTest.callback(matches[0]);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Err: {0}", ex);
                    break;
                }
            }
        }

        private void OnLogFileChanged(object sender, FileSystemEventArgs e)
        {
            if (Path.GetFileNameWithoutExtension(e.FullPath) != "DeadByDaylight")
                return;

            using (var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var stream = new StreamReader(fs))
            {
                var lines = new List<string>();

                var currentLine = "";
                var currentLineCount = 0;

                while(!stream.EndOfStream)
                {
                    currentLine = stream.ReadLine();
                    if(currentLineCount > _lastLine && _lastLine != -1)
                    {
                        ExecuteCallbackFromLine(currentLine);
                    } 
                    else if (_lastLine == -1 && currentLine.Contains("LogInit: Base Directory:"))
                    {
                        currentLine = currentLine.Replace("LogInit: Base Directory: ", "")
                            .Replace("/Binaries/Win64/", "")
                            .Replace("/", "\\");

                        DBDBaseDirectory = currentLine;
                    }
                    
                    currentLineCount++;
                }

                _lastLine = currentLineCount;
            }
        }

        private void FileChangedBad(object sender, FileSystemEventArgs e)
        {
            if (Path.GetFileNameWithoutExtension(e.FullPath) == "DeadByDaylight")
                _lastLine = -1;
        }

        private void AddCallback(string regex, RegexCallback.Callback callback)
        {
            _callbacks.Add(new RegexCallback
            {
                regex = new Regex(regex),
                callback = callback
            }); 
        }

        public async Task Run()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var path = Path.Combine(appData, "DeadByDaylight", "Saved", "Logs");
            if (!Directory.Exists(path))
                throw new Exception("Cannot find Dead by daylight path");

            await GetCharacters();
            await GetCustomizationItems();

            _watcher = new FileSystemWatcher();
            _watcher.Path = path;
            _watcher.NotifyFilter = NotifyFilters.LastAccess
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName;

            // Only watch text files.
            _watcher.Filter = "*.log";
            _watcher.Changed += OnLogFileChanged;
            _watcher.Created += FileChangedBad;
            _watcher.EnableRaisingEvents = true;

        }


        public DbdLobbyInfoReader()
        {
            _lastLine = -1;
            _httpClient = new ApiClient();
            _callbacks = new List<RegexCallback>();

            AddCallback(@"GameFlow:\sLoadingContextComponent::SetStartingMapUrl\(\)\sUrl=\'(.*?)\'", OnSetStartingMap);
            AddCallback(@"LogMirrors:\sSENDING\sREQUEST:\s\[GET\shttps\:\/\/.*?\/api\/v1\/match\/(.*?)\]", OnMatchRequest);
            AddCallback(@"LogCustomization:\s\-\-\>\s([a-zA-Z0-9_]+)", OnCustomizationSelect);
        }

    }
}