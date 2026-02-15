using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SophisticatedModManager.ViewModels;

namespace SophisticatedModManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // ==================== PROFILE SIDEBAR ====================

    private void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ProfileItem profile)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedProfile = profile;
            }
        }
    }

    // ==================== KEYBOARD HANDLERS ====================

    private void NewProfileNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            vm.ConfirmCreateProfileCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelCreateProfileCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void RenameProfileNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Key == Key.Enter)
        {
            vm.ConfirmRenameProfileCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRenameProfileCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ==================== CONTEXT MENU (HAMBURGER MENU) ====================

    private void HamburgerMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Tag is not ModEntryViewModel modVm) return;
        if (DataContext is not MainViewModel vm) return;

        var menu = new ContextMenu();

        if (!modVm.IsCommon)
        {
            var moveToCommon = new MenuItem { Header = "Move to Common" };
            moveToCommon.Click += (_, _) =>
            {
                vm.MoveModCommand.Execute(new MoveModRequest(modVm, "Common", null));
            };
            menu.Items.Add(moveToCommon);
        }

        foreach (var profile in vm.Profiles)
        {
            if (!modVm.IsCommon && profile.Name == vm.SelectedProfile?.Name)
                continue;

            var moveToProfile = new MenuItem { Header = $"Move to \"{profile.Name}\"" };
            var profileName = profile.Name;
            moveToProfile.Click += (_, _) =>
            {
                vm.MoveModCommand.Execute(new MoveModRequest(modVm, "Profile", profileName));
            };
            menu.Items.Add(moveToProfile);
        }

        {
            var duplicateMenu = new MenuItem { Header = "Duplicate to Profile" };
            foreach (var profile in vm.Profiles)
            {
                if (!modVm.IsCommon && profile.Name == vm.SelectedProfile?.Name)
                    continue;

                var dupItem = new MenuItem { Header = profile.Name };
                var targetName = profile.Name;
                dupItem.Click += (_, _) =>
                {
                    vm.DuplicateModCommand.Execute(new DuplicateModRequest(modVm, targetName));
                };
                duplicateMenu.Items.Add(dupItem);
            }
            if (duplicateMenu.Items.Count > 0)
                menu.Items.Add(duplicateMenu);
        }

        if (!modVm.IsCollection)
        {
            var collections = new List<(string name, string path)>();

            foreach (var name in vm.CommonCollectionNames)
            {
                var modsRoot = vm.GetModsRootPath();
                var collPath = System.IO.Path.Combine(modsRoot, name);
                collections.Add((name + " (Common)", collPath));
            }

            if (vm.SelectedProfile != null)
            {
                foreach (var name in vm.GetProfileCollectionNames(vm.SelectedProfile.Name))
                {
                    var modsRoot = vm.GetModsRootPath();
                    var collPath = System.IO.Path.Combine(modsRoot, vm.SelectedProfile.Name, name);
                    collections.Add((name, collPath));
                }
            }

            if (collections.Count > 0)
            {
                var moveToCollection = new MenuItem { Header = "Move to Collection" };
                foreach (var (collName, collPath) in collections)
                {
                    var item = new MenuItem { Header = collName };
                    var targetPath = collPath;
                    item.Click += (_, _) =>
                    {
                        vm.MoveModToCollection(modVm, targetPath);
                    };
                    moveToCollection.Items.Add(item);
                }
                menu.Items.Add(moveToCollection);
            }
        }

        // Convert collection to profile
        if (modVm.IsCollection)
        {
            menu.Items.Add(new Separator());
            var convertToProfile = new MenuItem { Header = "Convert to Profile..." };
            convertToProfile.Click += (_, _) =>
            {
                vm.ConvertCollectionToProfileCommand.Execute(modVm);
            };
            menu.Items.Add(convertToProfile);
        }

        // Share / Unshare options (only for non-common, non-sub-mod items)
        if (!modVm.IsCommon)
        {
            var isSubMod = false;
            // Check if this mod is a sub-mod of a collection
            if (vm.SelectedProfile != null)
            {
                foreach (var profileMod in vm.ProfileMods)
                {
                    if (profileMod.IsCollection && profileMod.SubMods.Contains(modVm))
                    {
                        isSubMod = true;
                        break;
                    }
                }
            }

            if (!isSubMod)
            {
                if (modVm.IsShared)
                {
                    var unshareItem = new MenuItem { Header = "Unshare (make private to this profile)" };
                    unshareItem.Click += (_, _) => vm.UnshareMod(modVm);
                    menu.Items.Add(unshareItem);
                }
                else if (vm.Profiles.Count >= 2)
                {
                    var shareItem = new MenuItem { Header = "Share across profiles..." };
                    shareItem.Click += (_, _) => vm.OpenShareDialog(modVm);
                    menu.Items.Add(shareItem);
                }
            }
        }

        menu.Items.Add(new Separator());
        var toggleItem = new MenuItem { Header = modVm.IsEnabled ? "Disable" : "Enable" };
        toggleItem.Click += (_, _) =>
        {
            modVm.IsEnabled = !modVm.IsEnabled;
        };
        menu.Items.Add(toggleItem);

        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    // ==================== DRAG AND DROP ====================

    // Mod Item Drag Initiation
    private void ModItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement element) return;
        if (element.Tag is not ModEntryViewModel modVm) return;

        DragDrop.DoDragDrop(element, modVm, DragDropEffects.Move);
    }

    // Drop on Sidebar Profiles
    private void SidebarProfile_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(ModEntryViewModel)))
            e.Effects = DragDropEffects.Move;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void SidebarProfile_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!e.Data.GetDataPresent(typeof(ModEntryViewModel))) return;

        ProfileItem? profile = null;
        if (sender is FrameworkElement el)
        {
            var current = el as DependencyObject;
            while (current != null)
            {
                if (current is Button btn && btn.Tag is ProfileItem pi)
                {
                    profile = pi;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            if (profile == null && el.DataContext is ProfileItem dc)
                profile = dc;
        }

        if (profile == null) return;

        var modVm = (ModEntryViewModel)e.Data.GetData(typeof(ModEntryViewModel))!;
        vm.MoveModCommand.Execute(new MoveModRequest(modVm, "Profile", profile.Name));
    }

    // Shared DragOver handler for mod sections (Common and Profile)
    private void ModSection_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(ModEntryViewModel)))
            e.Effects = DragDropEffects.Move;
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    // Drop on Common Mods Section
    private void CommonModsSection_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Data.GetDataPresent(typeof(ModEntryViewModel)))
        {
            var modVm = (ModEntryViewModel)e.Data.GetData(typeof(ModEntryViewModel))!;
            vm.MoveModCommand.Execute(new MoveModRequest(modVm, "Common", null));
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            vm.HandleExternalDrop(files, isCommon: true);
        }
    }

    private void ProfileModsSection_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (e.Data.GetDataPresent(typeof(ModEntryViewModel)))
        {
            var modVm = (ModEntryViewModel)e.Data.GetData(typeof(ModEntryViewModel))!;
            if (vm.SelectedProfile != null)
                vm.MoveModCommand.Execute(new MoveModRequest(modVm, "Profile", vm.SelectedProfile.Name));
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            vm.HandleExternalDrop(files, isCommon: false);
        }
    }

    // ==================== COLLECTION INTERACTION ====================

    private void CollectionExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ModEntryViewModel modVm && modVm.IsCollection)
        {
            modVm.IsExpanded = !modVm.IsExpanded;
        }
    }

    private void CollectionItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (sender is not FrameworkElement element) { e.Handled = true; return; }
        if (element.Tag is not ModEntryViewModel targetModVm) { e.Handled = true; return; }
        if (!targetModVm.IsCollection) { e.Handled = true; return; }
        if (!e.Data.GetDataPresent(typeof(ModEntryViewModel))) { e.Handled = true; return; }

        var draggedMod = (ModEntryViewModel)e.Data.GetData(typeof(ModEntryViewModel))!;

        if (draggedMod == targetModVm) { e.Handled = true; return; }
        if (draggedMod.IsCollection) { e.Handled = true; return; }
        if (targetModVm.SubMods.Contains(draggedMod)) { e.Handled = true; return; }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void CollectionItem_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is not FrameworkElement element) return;
        if (element.Tag is not ModEntryViewModel targetModVm) return;
        if (!targetModVm.IsCollection) return;
        if (!e.Data.GetDataPresent(typeof(ModEntryViewModel))) return;

        var draggedMod = (ModEntryViewModel)e.Data.GetData(typeof(ModEntryViewModel))!;

        if (draggedMod == targetModVm) return;
        if (draggedMod.IsCollection) return;
        if (targetModVm.SubMods.Contains(draggedMod)) return;

        vm.MoveModToCollection(draggedMod, targetModVm.Model.FolderPath);
        e.Handled = true;
    }

}
