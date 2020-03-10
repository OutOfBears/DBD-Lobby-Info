using DBD_Lobby_Info.WindowEvents;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using DBD_Magic;
using DBD_Magic.Responses;
using System.IO;

namespace DBD_Lobby_Info
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly DbdLobbyInfoReader _dbdLobbyInfoReader;

        public MainWindow()
        {
            InitializeComponent();

            SnapsToDevicePixels = true;
            DataContext = new WindowViewModel(this);

            _dbdLobbyInfoReader = new DbdLobbyInfoReader();
            _dbdLobbyInfoReader.OnKillerInfo += _dbdLobbyInfoReader_OnKillerInfo;
            _dbdLobbyInfoReader.OnMatchInfo += _dbdLobbyInfoReader_OnMatchInfo;
            _dbdLobbyInfoReader.LobbyCharacterCustomizationChanged += _dbdLobbyInfoReader_LobbyCharacterCustomizationChanged;
            _dbdLobbyInfoReader.OnLobbyLeave += _dbdLobbyInfoReader_OnLobbyLeave;

            _dbdLobbyInfoReader_OnLobbyLeave(null, null);
            Task.Run(_dbdLobbyInfoReader.Run);
        }

        private void _dbdLobbyInfoReader_OnMatchInfo(object sender, MatchInfoArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var viewContext = (DataContext as WindowViewModel);
                viewContext.Rank = e.Match.Rank.ToString();
                viewContext.MatchID = e.Match.MatchId.ToString();
                viewContext.OnPropertyChanged("MatchID");
                viewContext.OnPropertyChanged("Rank");
            });
        }

        private void _dbdLobbyInfoReader_OnKillerInfo(object sender, FriendsResponse e)
        {
            Dispatcher.Invoke(() =>
            {
                var viewContext = (DataContext as WindowViewModel);
                viewContext.CloudID = e.UserId.ToString();
                viewContext.SteamID = e.PlatformIds?.Steam;
                viewContext.Username = e.FriendPlayerName?.ProviderPlayerNames?.Steam;
                viewContext.OnPropertyChanged("CloudID");
                viewContext.OnPropertyChanged("SteamID");
                viewContext.OnPropertyChanged("Username");
            });
            //throw new NotImplementedException();
        }

        private string SearchIconRecursive(string baseDirectory, string iconName)
        {
            iconName += ".png";
            if (string.IsNullOrEmpty(baseDirectory))
                return WindowViewModel.MISSING_ICON;

            baseDirectory = System.IO.Path.Combine(baseDirectory, "Content", "UI", "Icons", "Customization");
            if (!Directory.Exists(baseDirectory))
                return WindowViewModel.MISSING_ICON;

            var icon = Directory.EnumerateFiles(baseDirectory, "*.*", SearchOption.AllDirectories)
                .FirstOrDefault(x => x.EndsWith(iconName));
            if (icon == null || icon.Equals(default))
                return WindowViewModel.MISSING_ICON;
            return icon;
        }

        private void _dbdLobbyInfoReader_OnLobbyLeave(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var viewModel = (DataContext as WindowViewModel);
                viewModel.Icon1 = WindowViewModel.MISSING_ICON;
                viewModel.Icon2 = WindowViewModel.MISSING_ICON;
                viewModel.Icon3 = WindowViewModel.MISSING_ICON;

                viewModel.Rank = "-1";
                viewModel.Killer = "Unknown";
                viewModel.Username = "Unknown";
                viewModel.SteamID = "Unknown";
                viewModel.CloudID = "Unknown";
                viewModel.MatchID = "Unknown";

                if (sender != null)
                {
                    viewModel.OnPropertyChanged("Icon1");
                    viewModel.OnPropertyChanged("Icon2");
                    viewModel.OnPropertyChanged("Icon3");
                    viewModel.OnPropertyChanged("Rank");
                    viewModel.OnPropertyChanged("Killer");
                    viewModel.OnPropertyChanged("Username");
                    viewModel.OnPropertyChanged("SteamID");
                    viewModel.OnPropertyChanged("CloudID");
                    viewModel.OnPropertyChanged("MatchID");
                }
            });
        }

        private void _dbdLobbyInfoReader_LobbyCharacterCustomizationChanged(object sender, CharacterCustomizationArgs e)
        {
            if (e.Character.Role != Role.EPlayerRoleVeSlasher)
                return;

            var baseDirectory = (sender as DbdLobbyInfoReader).DBDBaseDirectory;

            Dispatcher.Invoke(() =>
            {
                var viewModel = (DataContext as WindowViewModel);
                var icon = SearchIconRecursive(baseDirectory, e.Outfit);

                viewModel.Killer = e.Character.DisplayName;
                viewModel.OnPropertyChanged("Killer");

                switch (e.Item.Category)
                {
                    case Category.ECustomizationCategoryKillerHead:
                        viewModel.Icon1 = icon;
                        viewModel.OnPropertyChanged("Icon1");
                        break;

                    case Category.ECustomizationCategoryKillerBody:
                        viewModel.Icon2 = icon;
                        viewModel.OnPropertyChanged("Icon2");
                        break;

                    case Category.ECustomizationCategoryKillerWeapon:
                        viewModel.Icon3 = icon;
                        viewModel.OnPropertyChanged("Icon3");
                        break;

                    default:
                        break;
                }
            });
        }

        private void AppWindow_Deactivated(object sender, EventArgs e)
        {
            // Show overlay if we lose focus
            (DataContext as WindowViewModel).DimmableOverlayVisible = true;
        }

        private void AppWindow_Activated(object sender, EventArgs e)
        {
            // Hide overlay if we are focused
            (DataContext as WindowViewModel).DimmableOverlayVisible = false;
        }

    }
}
