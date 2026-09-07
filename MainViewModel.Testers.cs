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
    private void InitializeTesterCommands()
    {
        RedeemTesterCodeCommand=new RelayCommand(()=>_=RedeemTesterCodeAsync(),()=>!IsBusy&&_testers.Session is not null&&!string.IsNullOrWhiteSpace(TesterCode));
        CheckTesterAccessCommand=new RelayCommand(()=>_=CheckTesterAccessAsync(),()=>!IsBusy&&_testers.Session is not null);
        MergeCommand=new RelayCommand(()=>_=RunTesterOperationAsync("merge"),()=>!IsBusy&&HasTesterAccess&&DownloadsComplete);
        PlayCommand=new RelayCommand(()=>_=RunTesterOperationAsync("play"),()=>!IsBusy&&HasTesterAccess&&TesterBuildReady);
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
    private Task RunTesterOperationAsync(string operation)=>RunGuarded(operation=="merge"?"Preparing tester game":"Authorizing game launch",async ct=>
    {
        if(_steam is null || _authorization is null)throw new TesterException("Steam authorization is required.");
        _steam.RequireAccount(_authorization);
        await RequireTesterAccessAsync(ct).ConfigureAwait(false);
        if(operation=="play"&&!TesterBuildReady)throw new TesterException("A new game update requires a merge before Play.");
        await TesterPackageHost.RunAsync(_testers,_testerEnvelope!,operation,Config.StorageRoot,this,ct).ConfigureAwait(false);
        await RefreshTesterAccessAsync(ct).ConfigureAwait(false);
    });
}
