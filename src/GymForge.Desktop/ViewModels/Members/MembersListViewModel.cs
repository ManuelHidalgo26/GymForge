using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Enums;
using MediatR;
using System.Collections.ObjectModel;

namespace GymForge.Desktop.ViewModels.Members;

public partial class MembersListViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private Guid _companyId;
    private Guid _siteId;

    [ObservableProperty] private ObservableCollection<MemberDto> _members = [];
    [ObservableProperty] private MemberDto? _selectedMember;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private MemberStatus? _statusFilter;

    public const int PageSize = 50;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    public MembersListViewModel(IMediator mediator)
    {
        _mediator = mediator;
    }

    public void Initialize(Guid companyId, Guid siteId)
    {
        _companyId = companyId;
        _siteId = siteId;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(SearchText) && SearchText.Length >= 2)
            {
                var results = await _mediator.Send(
                    new SearchMembersQuery(SearchText, _companyId, _siteId, 100), ct);
                Members = new ObservableCollection<MemberDto>(results);
                TotalCount = results.Count;
            }
            else
            {
                var paged = await _mediator.Send(
                    new GetMembersQuery(_companyId, _siteId, CurrentPage, PageSize, StatusFilter), ct);
                Members = new ObservableCollection<MemberDto>(paged.Items);
                TotalCount = paged.TotalCount;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        CurrentPage = 1;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        StatusFilter = null;
        CurrentPage = 1;
        _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (value.Length == 0 || value.Length >= 2)
            _ = SearchAsync();
    }

    partial void OnStatusFilterChanged(MemberStatus? value)
    {
        CurrentPage = 1;
        _ = LoadAsync();
    }
}
