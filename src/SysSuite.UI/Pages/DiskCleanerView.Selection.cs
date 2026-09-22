using System.Windows;
using System.Windows.Controls;
using SysSuite.Core.Abstractions.System;

namespace SysSuite.UI.Pages;

public partial class DiskCleanerView
{
    private void OnGroupChecked(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { Tag: DiskGroup group })
        {
            return;
        }

        var groupRows = itemRows
            .Where(row => row.Group.Key == group.Key && row.CanSelect)
            .ToArray();
        var select = !groupRows.Any(row => selectedPaths.Contains(row.Path));
        foreach (var row in groupRows)
        {
            row.SetCheckedSilently(select);
        }

        if (select)
        {
            selectedPaths.UnionWith(groupRows.Select(row => row.Path));
        }
        else
        {
            foreach (var row in groupRows)
            {
                selectedPaths.Remove(row.Path);
            }
        }

        UpdateActionState();
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs args)
    {
        var selectedCategory = CategoryList.SelectedItem as DiskCategoryRow;
        var visibleRows = (selectedCategory is null || selectedCategory.Category.Length == 0
            ? itemRows
            : itemRows.Where(row => row.Category == selectedCategory.Category)).Where(row => row.CanSelect).ToArray();
        var select = visibleRows.Any(row => !selectedPaths.Contains(row.Path));
        foreach (var row in visibleRows)
        {
            row.SetCheckedSilently(select);
        }

        if (select)
        {
            selectedPaths.UnionWith(visibleRows.Select(row => row.Path));
        }
        else
        {
            selectedPaths.ExceptWith(visibleRows.Select(row => row.Path));
        }

        UpdateActionState();
    }
}
