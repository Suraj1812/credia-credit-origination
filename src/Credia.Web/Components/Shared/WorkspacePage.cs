using Credia.Application;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
namespace Credia.Web.Components.Shared;
public abstract class WorkspacePage : ComponentBase
{
    [Inject] protected ICreditWorkspace Workspace { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected ILogger<WorkspacePage> Logger { get; set; } = default!;
    protected Actor CurrentActor { get; private set; } = new(Guid.Empty, "", "");
    protected string? Error, Notice;
    protected bool Loading = true, Busy;
    protected override async Task OnInitializedAsync()
    {
        await Run(async () => { CurrentActor = Actor.From((await Authentication.GetAuthenticationStateAsync()).User); await LoadAsync(); });
        Loading = false;
    }
    protected virtual Task LoadAsync() => Task.CompletedTask;
    protected async Task Run(Func<Task> action, string? success = null)
    {
        if (Busy) return;
        Busy = true; Error = null; Notice = null;
        try { await action(); Notice = success; }
        catch (AppException ex) { Error = ex.Message; }
        catch (Exception ex) { Logger.LogError(ex, "Workspace operation failed"); Error = "No pudimos completar la operación. Vuelve a intentarlo."; }
        finally { Busy = false; }
    }
}
