using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;

namespace SophisticatedModManager.ViewModels;

/// <summary>
/// MainViewModel partial: Helper methods for UI operations and search filtering.
/// </summary>
public partial class MainViewModel
{
    private readonly HashSet<ModEntryViewModel> _collectionsExpandedBySearch = new();

    private void ApplyModSearchFilter(string searchText)
    {
        var profileView = CollectionViewSource.GetDefaultView(ProfileMods);
        var commonView = CollectionViewSource.GetDefaultView(CommonMods);

        if (string.IsNullOrWhiteSpace(searchText))
        {
            profileView.Filter = null;
            commonView.Filter = null;

            foreach (var mod in ProfileMods.Concat(CommonMods))
            {
                if (mod.IsCollection)
                    CollectionViewSource.GetDefaultView(mod.SubMods).Filter = null;
            }

            foreach (var mod in _collectionsExpandedBySearch)
            {
                mod.IsExpanded = false;
            }
            _collectionsExpandedBySearch.Clear();
        }
        else
        {
            profileView.Filter = obj => ModMatchesSearch((ModEntryViewModel)obj, searchText);
            commonView.Filter = obj => ModMatchesSearch((ModEntryViewModel)obj, searchText);

            foreach (var mod in ProfileMods.Concat(CommonMods))
            {
                if (!mod.IsCollection) continue;

                var subView = CollectionViewSource.GetDefaultView(mod.SubMods);
                if (ModDirectlyMatches(mod, searchText))
                {
                    subView.Filter = null;
                }
                else
                {
                    subView.Filter = obj => ModDirectlyMatches((ModEntryViewModel)obj, searchText);
                }

                if (ModMatchesSearch(mod, searchText) && !mod.IsExpanded)
                {
                    mod.IsExpanded = true;
                    _collectionsExpandedBySearch.Add(mod);
                }
            }
        }
    }

    [RelayCommand]
    private void ExpandAllCollections()
    {
        foreach (var mod in ProfileMods.Concat(CommonMods))
        {
            if (mod.IsCollection)
                mod.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAllCollections()
    {
        foreach (var mod in ProfileMods.Concat(CommonMods))
        {
            if (mod.IsCollection)
                mod.IsExpanded = false;
        }
        _collectionsExpandedBySearch.Clear();
    }

    [RelayCommand]
    private void SwapModSections()
    {
        CommonModsOnTop = !CommonModsOnTop;
    }

    private static bool ModDirectlyMatches(ModEntryViewModel mod, string searchText)
    {
        return mod.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
               || mod.Author.Contains(searchText, StringComparison.OrdinalIgnoreCase)
               || mod.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ModMatchesSearch(ModEntryViewModel mod, string searchText)
    {
        if (ModDirectlyMatches(mod, searchText))
            return true;

        if (mod.IsCollection)
        {
            foreach (var subMod in mod.SubMods)
            {
                if (ModDirectlyMatches(subMod, searchText))
                    return true;
            }
        }

        return false;
    }

    partial void OnModSearchTextChanged(string value)
    {
        ApplyModSearchFilter(value);
    }
}
