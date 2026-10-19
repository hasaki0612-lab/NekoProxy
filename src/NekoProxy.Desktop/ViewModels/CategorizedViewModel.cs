using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace NekoProxy.Desktop.ViewModels;
public partial class CategorizedViewModel : ObservableObject
{
    [ObservableProperty] private int categoryIndex;
    public bool Category0 => CategoryIndex == 0; public bool Category1 => CategoryIndex == 1;
    public bool Category2 => CategoryIndex == 2; public bool Category3 => CategoryIndex == 3;
    public bool Category4 => CategoryIndex == 4;
    partial void OnCategoryIndexChanged(int value) { foreach(var n in new[]{nameof(Category0),nameof(Category1),nameof(Category2),nameof(Category3),nameof(Category4)}) OnPropertyChanged(n); }
    [RelayCommand] private void SelectCategory(string value) { if(int.TryParse(value,out var index) && index is >= 0 and <= 4) CategoryIndex=index; }
}
