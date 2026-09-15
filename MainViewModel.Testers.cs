using System.IO;
using System.Text.Json;
using System.Windows.Input;
using KpcLauncher.Core;

namespace KpcLauncher;

public sealed partial class MainViewModel
{
    private readonly TesterClient _testers = new();
    private SignedRelease? _testerEnvelope;
    private TesterRelease? _testerRelease;
    private bool _hasTesterAccess;
    private string _testerCode = "";
    private string _testerAccessStatus = "Sign in to check tester access";
    public string TesterCode { get=>_testerCode; set {if(Set(ref _testerCode,value))Requery();} }
    public string TesterAccessStatus {get=>_testerAccessStatus;private set=>Set(ref _testerAccessStatus,value);}
    public bool HasTesterAccess=>_hasTesterAccess;
    public bool TesterBuildReady
    {
        get
        {
            if(_testerRelease is null)return false;
            try
            {
                var stamp=Path.Combine(Config.StorageRoot,"KurtzPel-Tester",".tester-build.json");
                if(!File.Exists(stamp)||new FileInfo(stamp).Length>16384)return false;
                using var document=JsonDocument.Parse(File.ReadAllText(stamp));
                return document.RootElement.GetProperty("mergeVersion").GetString()==_testerRelease.MergeVersion;
            }
            catch(Exception ex) when(ex is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException){return false;}
        }
    }
    public string MergeButtonText=>TesterBuildReady?"Rebuild game":"Merge";
    public ICommand RedeemTesterCodeCommand {get;private set;}=null!;
    public ICommand CheckTesterAccessCommand {get;private set;}=null!;
    public ICommand MergeCommand {get;private set;}=null!;
    public ICommand PlayCommand {get;private set;}=null!;
    public ICommand ExportCharacterCommand {get;private set;}=null!;
    public ICommand OpenExportsFolderCommand {get;private set;}=null!;
    public ICommand DeleteAccountCommand {get;private set;}=null!;
    private void InitializeTesterCommands()
    {
        RedeemTesterCodeCommand=new RelayCommand(()=>_=RedeemTesterCodeAsync(),()=>!IsBusy&&_testers.Session is not null&&!string.IsNullOrWhiteSpace(TesterCode));
        CheckTesterAccessCommand=new RelayCommand(()=>_=CheckTesterAccessAsync(),()=>!IsBusy&&_testers.Session is not null);
        MergeCommand=new RelayCommand(()=>_=RunTesterOperationAsync("merge"),()=>!IsBusy&&HasTesterAccess&&DownloadsComplete);
        PlayCommand=new RelayCommand(()=>_=RunTesterOperationAsync("play"),()=>!IsBusy&&HasTesterAccess&&TesterBuildReady);
        ExportCharacterCommand=new RelayCommand(()=>_=RunTesterOperationAsync("export-character"),()=>!IsBusy&&HasTesterAccess&&_testerRelease?.CharacterTransferVersion==1);
        OpenExportsFolderCommand=new RelayCommand(()=>OpenFolder(Path.Combine(Config.StorageRoot,"Character Exports"),create:true));
        DeleteAccountCommand=new RelayCommand(()=>_=DeleteAccountAsync(),()=>!IsBusy&&_testers.Session is not null);
    }
    private async Task DeleteAccountAsync()
    {
        var session=_testers.Session;
        if(session is null)return;
        var confirmed=System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow,
            "Permanently delete all characters, items, currency, levels and progress on this community server for Steam account " + session.SteamId +
            "?\n\nThis cannot be undone. Close KurtzPel first.\n\nYour local character exports and tester access will be kept. You can use an export when you start again.",
            "Delete account",System.Windows.MessageBoxButton.YesNo,System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No)==System.Windows.MessageBoxResult.Yes;
        if(!confirmed)return;
        await RunGuarded("Deleting game account",async ct=>
        {
            if(_testers.Session!=session)throw new TesterException("The signed-in account changed. Please try again.");
            await _testers.DeleteAccountAsync(ct).ConfigureAwait(false);
            Log_("Game account deleted. Local character exports are available for your next Play.",LogLevel.Good);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>
                System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow,
                    "Account deleted. Press Play to start again. Your exported character files are still available.",
                    "Account deleted",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Information));
        });
    }
    private void ClearTesterAccess()
    {
        _hasTesterAccess=false;_testerEnvelope=null;_testerRelease=null;
        TesterAccessStatus=_testers.Session is null?"Sign in to check tester access":"Tester access has not been checked";
        RefreshTesterProperties();
    }
    private void RefreshTesterProperties()
    {
        OnPropertyChanged(nameof(HasTesterAccess));OnPropertyChanged(nameof(TesterBuildReady));OnPropertyChanged(nameof(MergeButtonText));Requery();
    }
    private Task CheckTesterAccessAsync()=>RunGuarded("Checking tester access",async ct=>
    {
        await RefreshTesterAccessAsync(ct).ConfigureAwait(false);
        await PrepareLocalTesterDataAsync(ct, refreshAccess:false).ConfigureAwait(false);
    });
    private async Task RefreshTesterAccessAsync(CancellationToken ct)
    {
        ClearTesterAccess();
        try
        {
            var status=await _testers.StatusAsync(ct).ConfigureAwait(false);
            if(status.SteamId!=_testers.Session?.SteamId)throw new TesterException("The server returned a different Steam account.");
            if(status.Tester&&status.Release is not null)
            {
                _testerRelease=TesterPackageHost.VerifyRelease(status.Release);_testerEnvelope=status.Release;_hasTesterAccess=true;
                TesterAccessStatus=TesterBuildReady?"Tester access active · game ready":"Tester access active · merge required";
            }
            else TesterAccessStatus="No tester access · enter your code in Settings";
        }
        catch {TesterAccessStatus="Tester service unavailable or sign-in required";throw;}
        finally {_ui.Post(()=>{RefreshTesterProperties();RefreshFacts();});}
    }
    private Task RedeemTesterCodeAsync()=>RunGuarded("Redeeming tester code",async ct=>
    {
        var code=TesterCode.Trim();TesterCode="";
        await _testers.RedeemAsync(code,ct).ConfigureAwait(false);
        Log_("Tester access linked to your Steam account.",LogLevel.Good);
        await RefreshTesterAccessAsync(ct).ConfigureAwait(false);
        await PrepareLocalTesterDataAsync(ct, refreshAccess:false).ConfigureAwait(false);
    });
    private Task OnArchiveReadyAsync(ArchiveSpec archive,CancellationToken ct) =>
        archive == LauncherConfig.RequiredArchives[0] ? PrepareLocalTesterDataAsync(ct) : Task.CompletedTask;

    private async Task PrepareLocalTesterDataAsync(CancellationToken ct,bool refreshAccess=true)
    {
        // Optional preparation cannot turn a successful normal Steam download into a failure.
        if(_testers.Session is null || _authorization is null || _steam is null)return;
        try
        {
            if(refreshAccess)await RefreshTesterAccessAsync(ct).ConfigureAwait(false);
            if(!HasTesterAccess || _testerEnvelope is null || _testerRelease?.KeyAcquisition is not { } request)return;
            if(!PreservationPipeline.HasVerifiedReceipt(Config,request.ArchiveManifest))return;
            if(_testers.Session?.SteamId!=_authorization.SteamId.ToString())return;
            _steam.RequireAccount(_authorization);
            await TesterPackageHost.RunAsync(_testers,_testerEnvelope,request.Operation,Config.StorageRoot,this,ct).ConfigureAwait(false);
            Log_("Local tester data is ready. It will be acquired again in memory when merging.",LogLevel.Good);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception ex){Log_("Tester data preparation could not finish: "+ex.Message+" Normal Steam downloads remain available.",LogLevel.Warn);}
    }
    private async Task RequireTesterAccessAsync(CancellationToken ct)
    {
        await RefreshTesterAccessAsync(ct).ConfigureAwait(false);
        if(!HasTesterAccess || _testerEnvelope is null)throw new TesterException("An active tester code is required.");
        if(_authorization is null || _testers.Session?.SteamId!=_authorization.SteamId.ToString())
            throw new TesterException("Authorize the same Steam account before continuing.");
    }
    private Task RunTesterOperationAsync(string operation)=>RunGuarded(operation switch {"merge"=>"Preparing tester game","export-character"=>"Preparing character export",_=>"Authorizing game launch"},async ct=>
    {
        if(_steam is null || _authorization is null)throw new TesterException("Steam authorization is required.");
        _steam.RequireAccount(_authorization);
        await RequireTesterAccessAsync(ct).ConfigureAwait(false);
        if(operation=="play"&&!TesterBuildReady)throw new TesterException("A new game update requires a merge before Play.");
        if(operation=="export-character"&&_testerRelease?.CharacterTransferVersion!=1)
            throw new TesterException("This test server has not enabled character exports yet.");
        if(operation=="play"&&_testerRelease?.CharacterTransferVersion==1)
            await OfferCharacterImportAsync(ct).ConfigureAwait(false);
        var capturedName=await TesterPackageHost.RunAsync(_testers,_testerEnvelope!,operation,Config.StorageRoot,this,ct).ConfigureAwait(false);
        if(capturedName is not null)
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>
                System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow,
                    "Captured " + capturedName, "Character export", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information));
        await RefreshTesterAccessAsync(ct).ConfigureAwait(false);
    });
    private async Task OfferCharacterImportAsync(CancellationToken ct)
    {
        var status=await _testers.CharacterCreationAsync(ct).ConfigureAwait(false);
        CharacterExportFile.ValidateStatus(status);
        if(status.State!="empty")return;
        var file=CharacterExportFile.FindLatest(Config.StorageRoot);
        if(file is null)return;
        var use=await System.Windows.Application.Current.Dispatcher.InvokeAsync(()=>
            System.Windows.MessageBox.Show(System.Windows.Application.Current.MainWindow,
                "There is an exported character file. Would you wish to use it for character creation?",
                "Character creation",System.Windows.MessageBoxButton.YesNo,System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.No)==System.Windows.MessageBoxResult.Yes);
        ct.ThrowIfCancellationRequested();
        if(!use)return;
        using var exported=await CharacterExportFile.ReadAsync(file,ct).ConfigureAwait(false);
        var imported=await _testers.ImportCharacterAsync(exported.RootElement,ct).ConfigureAwait(false);
        CharacterExportFile.ValidateStatus(imported);
        if(imported.State!="import-pending")throw new TesterException("The server did not prepare the imported character.");
        Log_("Imported character prepared. Choose your name and finish character creation in the game.",LogLevel.Good);
    }
}
