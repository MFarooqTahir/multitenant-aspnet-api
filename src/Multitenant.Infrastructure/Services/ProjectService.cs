using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common.Paging;
using Multitenant.Application.Common.Search;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Projects;
using Multitenant.Application.Errors.Exceptions;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Services;

/// <summary>
/// Projects of the current tenant. The tenant filter in <see cref="AppDbContext"/>
/// scopes every query, so this service never adds its own <c>TenantId</c> predicate.
/// </summary>
public sealed class ProjectService(
    AppDbContext dbContext,
    ITenantContext tenantContext) : IProjectService
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "status",
        "createdat",
        "updatedat",
    };

    public async Task<PagedResult<ProjectResponse>> ListAsync(ProjectQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Pagination.Normalize(query.Page, query.PageSize);
        var projects = dbContext.Projects.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = SearchPattern.Contains(query.Search);
            projects = projects.Where(x =>
                EF.Functions.ILike(x.Name, search, SearchPattern.EscapeCharacter)
                || EF.Functions.ILike(x.Description ?? string.Empty, search, SearchPattern.EscapeCharacter));
        }

        if (query.Status is not null)
        {
            projects = projects.Where(x => x.Status == query.Status);
        }

        var totalCount = await projects.CountAsync(cancellationToken);
        var (field, descending) = Sorting.Parse(query.Sort, SortFields, "createdat");

        projects = field switch
        {
            "name" => descending ? projects.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id) : projects.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "status" => descending ? projects.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id) : projects.OrderBy(x => x.Status).ThenBy(x => x.Id),
            "updatedat" => descending ? projects.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id) : projects.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            _ => descending ? projects.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id) : projects.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
        };

        var items = await projects
            .Skip(Pagination.Skip(page, pageSize))
            .Take(pageSize)
            .Select(x => ToResponse(x))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProjectResponse>(items, page, pageSize, totalCount);
    }

    public async Task<ProjectResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Project not found.");

        return ToResponse(project);
    }

    public async Task<ProjectResponse> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();

        var project = new Project
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Description = NormalizeDescription(request.Description),
            Status = request.Status,
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(project);
    }

    public async Task<ProjectResponse> UpdateAsync(
        Guid id,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Project not found.");

        project.Name = request.Name.Trim();
        project.Description = NormalizeDescription(request.Description);
        project.Status = request.Status;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(project);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Project not found.");

        dbContext.Projects.Remove(project);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static ProjectResponse ToResponse(Project project) =>
        new(
            project.Id,
            project.Name,
            project.Description,
            project.Status,
            project.CreatedAtUtc,
            project.UpdatedAtUtc);
}
