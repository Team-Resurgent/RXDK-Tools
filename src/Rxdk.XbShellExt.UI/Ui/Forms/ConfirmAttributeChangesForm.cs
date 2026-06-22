using Rxdk.XbShellExt.Ui;

namespace Rxdk.XbShellExt.Ui.Forms;

public sealed partial class ConfirmAttributeChangesForm : ShellDialogForm
{
    public bool ApplyRecursively { get; private set; }

    public ConfirmAttributeChangesForm()
    {
        InitializeComponent();
        ApplyRuntimeChrome();
        DesignPreview.ApplyIfDesignTime(() =>
        {
            introLabel.Text = "You have chosen to make the following attribute change(s):";
            changesTextBox.Text = DesignPreview.SampleAttributeChangesSummary;
            questionLabel.Text =
                "Do you want to apply this change to this folder only, or do you want to apply it to all subfolders and files as well?";
            folderOnlyRadio.Text = "Apply changes to this folder only";
            recursiveRadio.Text = "Apply changes to this folder, subfolders and files";
        });
    }

    public ConfirmAttributeChangesForm(string changeSummary, string scopeLabel, bool multiItemSelection)
        : this()
    {
        var scope = multiItemSelection ? "the selected items" : scopeLabel;

        introLabel.Text = "You have chosen to make the following attribute change(s):";
        changesTextBox.Text = changeSummary;
        questionLabel.Text =
            $"Do you want to apply this change to {scope} only, or do you want to apply it to all subfolders and files as well?";
        folderOnlyRadio.Text = $"Apply changes to {scope} only";
        recursiveRadio.Text = $"Apply changes to {scope}, subfolders and files";

        okButton.Click += (_, _) => ApplyRecursively = recursiveRadio.Checked;
    }
}
