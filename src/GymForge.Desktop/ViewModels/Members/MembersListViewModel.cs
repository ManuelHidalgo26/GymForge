using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Members;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace GymForge.Desktop.ViewModels.Members;

public partial class MembersListViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;
    private Guid CompanyId => _session.CompanyId;
    private Guid SiteId => _session.SiteId;

    [ObservableProperty] private ObservableCollection<MemberDto> _members = [];
    [ObservableProperty] private MemberDto? _selectedMember;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _currentPage = 1;

    [ObservableProperty] private MemberStatus? _statusFilter;
    [ObservableProperty] private string? _errorMessage;

    // Importación de padrón (CSV)
    [ObservableProperty] private string? _importMessage;
    [ObservableProperty] private bool _isImporting;

    public const int PageSize = 50;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
    public bool CanGoNext => CurrentPage < TotalPages;
    public bool CanGoPrevious => CurrentPage > 1;

    /// <summary>Estado vacío: sin resultados y sin carga en curso.</summary>
    public bool IsEmpty => !IsLoading && Members.Count == 0;

    /// <summary>Hay búsqueda o filtro activo → el vacío es "sin resultados", no "sin datos".</summary>
    public bool HasActiveFilter => !string.IsNullOrWhiteSpace(SearchText) || StatusFilter is not null;

    public string EmptyTitle => HasActiveFilter ? "Sin resultados" : "Todavía no hay socios";
    public string EmptyBody => HasActiveFilter
        ? "No encontramos socios con esos filtros. Probá otro término o limpiá la búsqueda."
        : "Creá el primero con «Nuevo socio» para empezar.";

    /// <summary>Fired when the user opens a member's detail card.</summary>
    public event Action<MemberDto>? OpenDetailRequested;

    /// <summary>Fired when the user clicks "Nuevo socio".</summary>
    public event Action? CreateMemberRequested;

    /// <summary>Fired when the user clicks "Importar": el code-behind abre el selector de archivo.</summary>
    public event Action? ImportRequested;

    public MembersListViewModel(IMediator mediator, SessionContext session)
    {
        _mediator = mediator;
        _session  = session;
    }

    // ── Load ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 2)
            {
                var results = await _mediator.Send(
                    new SearchMembersQuery(SearchText, CompanyId, SiteId, 100), ct);
                Members = new ObservableCollection<MemberDto>(results);
                TotalCount = results.Count;
            }
            else
            {
                var paged = await _mediator.Send(
                    new GetMembersQuery(CompanyId, SiteId, CurrentPage, PageSize, StatusFilter), ct);
                Members = new ObservableCollection<MemberDto>(paged.Items);
                TotalCount = paged.TotalCount;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Detail navigation ─────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenDetail(MemberDto? member)
    {
        if (member is null) return;
        OpenDetailRequested?.Invoke(member);
    }

    [RelayCommand]
    private void CreateMember() => CreateMemberRequested?.Invoke();

    [RelayCommand]
    private void Import() => ImportRequested?.Invoke();

    /// <summary>
    /// Parsea el CSV elegido y da de alta el padrón. Lo llama el code-behind tras
    /// leer el archivo (la lectura/selección es responsabilidad de la vista).
    /// </summary>
    public async Task ImportFromCsvAsync(string csvText)
    {
        ImportMessage = null;
        IsImporting = true;
        try
        {
            var parsed = MemberCsvParser.Parse(csvText);
            if (parsed.Rows.Count == 0)
            {
                ImportMessage = "No se encontraron socios para importar. " + string.Join(" ", parsed.Errors);
                return;
            }

            var result = await _mediator.Send(new ImportMembersCommand(CompanyId, SiteId, parsed.Rows));

            var summary = $"Importados: {result.Imported}." +
                          (result.Skipped > 0 ? $" Omitidos: {result.Skipped}." : string.Empty);
            var details = parsed.Errors.Concat(result.Errors).Take(8).ToList();
            ImportMessage = details.Count > 0
                ? summary + "\n" + string.Join("\n", details)
                : summary;

            CurrentPage = 1;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ImportMessage = ex.Message;
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task DeleteMemberAsync(MemberDto? member)
    {
        if (member is null) return;
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new DeleteMemberCommand(member.Id));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── Pagination & search ───────────────────────────────────────────────────

    [RelayCommand]
    private async Task SearchAsync()
    {
        CurrentPage = 1;
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync()
    {
        CurrentPage++;
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private async Task PreviousPageAsync()
    {
        CurrentPage--;
        await LoadAsync();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText   = string.Empty;
        StatusFilter = null;
        CurrentPage  = 1;
        _ = LoadAsync();
    }

    partial void OnMembersChanged(ObservableCollection<MemberDto> value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(TotalPages));

    partial void OnSearchTextChanged(string value)
    {
        NotifyEmptyCopy();
        if (value.Length == 0 || value.Length >= 2)
            _ = SearchAsync();
    }

    partial void OnStatusFilterChanged(MemberStatus? value)
    {
        NotifyEmptyCopy();
        CurrentPage = 1;
        _ = LoadAsync();
    }

    private void NotifyEmptyCopy()
    {
        OnPropertyChanged(nameof(HasActiveFilter));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyBody));
    }
}
