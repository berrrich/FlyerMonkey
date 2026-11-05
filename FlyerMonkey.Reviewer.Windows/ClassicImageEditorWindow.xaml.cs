using FlyerMonkey.Reviewer.Windows.Models;
using FlyerMonkey.Reviewer.Windows.Services;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FlyerMonkey.Reviewer.Windows;

public partial class ClassicImageEditorWindow : Window
{
    private readonly ClassicArtworkWorkflow _workflow;
    private readonly CancellationToken _lifetime;
    private readonly Func<string,bool,OperationSession?>? _begin;
    private readonly Func<CancellationToken>? _parentCancellation;
    private CancellationTokenSource? _generation;
    private bool _closed;

    public ClassicImageEditorWindow(ExtractedProduct product, string imageFolder, CancellationToken lifetime,
        Func<string, CancellationToken, Task<byte[]>>? generate = null,
        Func<string,bool,OperationSession?>? begin = null, Func<CancellationToken>? parentCancellation = null, ArtworkArchive? archive = null)
    {
        InitializeComponent();
        _lifetime = lifetime; _begin = begin; _parentCancellation = parentCancellation;
        _workflow = new ClassicArtworkWorkflow(product, archive ?? new ArtworkArchive(imageFolder, generate == null ? "OpenAI / gpt-image-1.5" : "Injected provider"), generate ?? ((prompt, token) => new ClassicImageGenerationService().GenerateImageAsync(prompt, token)));
        ProductHeading.Text = product.ProductName;
        if (!string.IsNullOrWhiteSpace(product.ImageFilePath) && File.Exists(product.ImageFilePath))
        {
            try { OriginalImage.Source = Decode(File.ReadAllBytes(product.ImageFilePath)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
            { StatusText.Text = "The working image could not be previewed. Its reference is kept until replacement is accepted."; }
        }
        Closed += (_, _) => { _closed = true; _workflow.Close(); _generation?.Cancel(); };
    }
    private static BitmapImage Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); var image = new BitmapImage();
        image.BeginInit(); image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
    private async void Generate_Click(object sender,RoutedEventArgs e)
    {
        if(_generation!=null)return;
        using var operation = _begin?.Invoke("Generating Crater image",true);
        if(_begin!=null && operation==null)return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime,_parentCancellation?.Invoke()??CancellationToken.None);
        _generation=cancellation;GenerateButton.IsEnabled=AcceptButton.IsEnabled=Instructions.IsEnabled=false;CancelGenerationButton.IsEnabled=true;ProposedImage.Source=null;
        StatusText.Text="Generating correction proposal. The current working image is unchanged.";
        try
        {
            await _workflow.GenerateAsync(Instructions.Text,false,cancellation.Token, operation == null ? null : operation.Report);
            operation?.Success("Correction proposal archived. Accept or reject it; current working image is unchanged.");
            if(_closed)return;
            ProposedImage.Source=Decode(File.ReadAllBytes(_workflow.ProposedPath!));AcceptButton.IsEnabled=true;
            StatusText.Text="Proposal archived. Accept replaces the working image; Reject keeps it. Save the draft afterward.";
        }
        catch(OperationCanceledException) { operation?.Cancel("Local image processing cancelled. Provider cancellation is uncertain; completed returned artwork is archived.");if(!_closed)StatusText.Text="Cancelled locally. Provider processing may already have occurred; returned completed artwork is archived. Working image unchanged."; }
        catch(Exception ex) {operation?.Fail(ex.Message);if(!_closed)StatusText.Text=ex.Message;}
        finally {_generation=null;if(!_closed){GenerateButton.IsEnabled=Instructions.IsEnabled=true;CancelGenerationButton.IsEnabled=false;}}
    }
    private async void Accept_Click(object sender,RoutedEventArgs e)
    {
        if(_generation!=null || !AcceptButton.IsEnabled)return;
        using var operation=_begin?.Invoke("Accepting image",false);
        if(_begin!=null && operation==null)return;
        AcceptButton.IsEnabled=GenerateButton.IsEnabled=RejectButton.IsEnabled=false;
        try {await _workflow.AcceptAsync();operation?.Success("Working image replaced; archive retained. Save to persist the draft reference.");if(!_closed)DialogResult=true;}
        catch(OperationCanceledException){operation?.Cancel("Image edit closed without accepting the replacement.");}
        catch(Exception ex){operation?.Fail(ex.Message);if(!_closed){StatusText.Text=ex.Message;AcceptButton.IsEnabled=_workflow.HasProposal;GenerateButton.IsEnabled=RejectButton.IsEnabled=true;}}
    }
    private void CancelGeneration_Click(object sender,RoutedEventArgs e)=>_generation?.Cancel();
    private void Reject_Click(object sender,RoutedEventArgs e){_workflow.Close();DialogResult=false;}
}
