using KapeIR.Ui.Dialogs;

namespace KapeIR.Builder.Tests;

public sealed class DialogServiceContractTests
{
    [Fact]
    public void DialogIcon_HasExpectedMembers()
    {
        Assert.Equal(
            new[] { "None", "Info", "Warning", "Error", "Question" },
            Enum.GetNames<DialogIcon>());
    }

    [Fact]
    public void FakeTriageDialogService_ImplementsSharedContract()
    {
        IDialogService dialogs = new FakeTriageDialogService { NextConfirm = false, NextFolder = @"D:\out" };
        Assert.False(dialogs.Confirm("?", "t"));
        Assert.Equal(@"D:\out", dialogs.PickFolder("t"));
        Assert.Null(dialogs.PickOpenFile("t", "All|*.*"));
    }
}
