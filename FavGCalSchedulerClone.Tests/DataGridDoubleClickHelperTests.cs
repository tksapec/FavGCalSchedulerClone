using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class DataGridDoubleClickHelperTests
{
    [Fact]
    public void IsEditableRowTypeName_ReturnsTrueForDataGridRow()
    {
        Assert.True(DataGridDoubleClickHelper.IsEditableRowTypeName("DataGridRow"));
        Assert.True(DataGridDoubleClickHelper.IsEditableRowTypeName("System.Windows.Controls.DataGridRow"));
    }

    [Fact]
    public void IsEditableRowTypeName_ReturnsFalseForBlankOrHeaderTargets()
    {
        Assert.False(DataGridDoubleClickHelper.IsEditableRowTypeName(null));
        Assert.False(DataGridDoubleClickHelper.IsEditableRowTypeName("DataGridColumnHeader"));
        Assert.False(DataGridDoubleClickHelper.IsEditableRowTypeName("ScrollBar"));
    }
}
