using Microsoft.EntityFrameworkCore;
using Multitenant.Application.Abstractions.Tenancy;
using Multitenant.Application.Common.Paging;
using Multitenant.Application.Contracts.Common;
using Multitenant.Application.Contracts.Tasks;
using Multitenant.Application.Errors.Exceptions;
using Multitenant.Domain.Entities;
using Multitenant.Infrastructure.Persistence;

namespace Multitenant.Infrastructure.Services;

public sealed class TaskService(
    AppDbContext dbContext,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : ITaskService
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "title",
        "status",
        "priority",
        "duedate",
        "createdat",
        "updatedat",
    };

    public async Task<PagedResult<TaskResponse>> ListAsync(TaskQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Pagination.Normalize(query.Page, query.PageSize);
        var tasks = dbContext.WorkItems.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = $"%{query.Search.Trim()}%";
            tasks = tasks.Where(x =>
                EF.Functions.ILike(x.Title, search)
                || EF.Functions.ILike(x.Description ?? string.Empty, search));
        }

        if (query.Status is not null)
        {
            tasks = tasks.Where(x => x.Status == query.Status);
        }

        if (query.Priority is not null)
        {
            tasks = tasks.Where(x => x.Priority == query.Priority);
        }

        if (query.ProjectId is not null)
        {
            tasks = tasks.Where(x => x.ProjectId == query.ProjectId);
        }

        if (query.AssigneeUserId is not null)
        {
            tasks = tasks.Where(x => x.AssigneeUserId == query.AssigneeUserId);
        }

        if (query.DueBefore is not null)
        {
            tasks = tasks.Where(x => x.DueDate != null && x.DueDate <= query.DueBefore);
        }

        if (query.DueAfter is not null)
        {
            tasks = tasks.Where(x => x.DueDate != null && x.DueDate >= query.DueAfter);
        }

        var totalCount = await tasks.CountAsync(cancellationToken);
        var (field, descending) = Sorting.Parse(query.Sort, SortFields, "createdat");

        tasks = field switch
        {
            "title" => descending ? tasks.OrderByDescending(x => x.Title) : tasks.OrderBy(x => x.Title),
            "status" => descending ? tasks.OrderByDescending(x => x.Status) : tasks.OrderBy(x => x.Status),
            "priority" => descending ? tasks.OrderByDescending(x => x.Priority) : tasks.OrderBy(x => x.Priority),
            "duedate" => descending ? tasks.OrderByDescending(x => x.DueDate) : tasks.OrderBy(x => x.DueDate),
            "updatedat" => descending ? tasks.OrderByDescending(x => x.UpdatedAtUtc) : tasks.OrderBy(x => x.UpdatedAtUtc),
            _ => descending ? tasks.OrderByDescending(x => x.CreatedAtUtc) : tasks.OrderBy(x => x.CreatedAtUtc),
        };

        var items = await tasks
            .Skip(Pagination.Skip(page, pageSize))
            .Take(pageSize)
            .Select(x => ToResponse(x))
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskResponse>(items, page, pageSize, totalCount);
    }

    public async Task<TaskResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await dbContext.WorkItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Task not found.");

        return ToResponse(task);
    }

    public async Task<TaskResponse> CreateAsync(
        Guid projectId,
        CreateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new UnauthorizedException();

        var projectExists = await dbContext.Projects
            .AnyAsync(x => x.Id == projectId, cancellationToken);

        if (!projectExists)
        {
            throw new NotFoundException("Project not found.");
        }

        await EnsureAssigneeBelongsToTenantAsync(request.AssigneeUserId, cancellationToken);

        var task = new WorkItem
        {
            TenantId = tenantId,
            ProjectId = projectId,
            Title = request.Title.Trim(),
            Description = NormalizeDescription(request.Description),
            Status = request.Status,
            Priority = request.Priority,
            AssigneeUserId = request.AssigneeUserId,
            AssigneeTenantId = request.AssigneeUserId is null ? null : tenantId,
            DueDate = request.DueDate,
            CreatedAtUtc = timeProvider.GetUtcNow(),
            UpdatedAtUtc = timeProvider.GetUtcNow(),
        };

        dbContext.WorkItems.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(task);
    }

    public async Task<TaskResponse> UpdateAsync(
        Guid id,
        UpdateTaskRequest request,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.WorkItems
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Task not found.");

        await EnsureAssigneeBelongsToTenantAsync(request.AssigneeUserId, cancellationToken);

        task.Title = request.Title.Trim();
        task.Description = NormalizeDescription(request.Description);
        task.Status = request.Status;
        task.Priority = request.Priority;
        task.AssigneeUserId = request.AssigneeUserId;
        task.AssigneeTenantId = request.AssigneeUserId is null ? null : task.TenantId;
        task.DueDate = request.DueDate;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(task);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await dbContext.WorkItems
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Task not found.");

        dbContext.WorkItems.Remove(task);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The global query filter already scopes <see cref="AppUser"/> to the current
    /// tenant, so a user id from another tenant simply resolves to no row here.
    /// </summary>
    private async Task EnsureAssigneeBelongsToTenantAsync(Guid? assigneeUserId, CancellationToken cancellationToken)
    {
        if (assigneeUserId is null)
        {
            return;
        }

        var exists = await dbContext.Users
            .AnyAsync(x => x.Id == assigneeUserId && x.IsActive, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Assignee not found.");
        }
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static TaskResponse ToResponse(WorkItem task) =>
        new(
            task.Id,
            task.ProjectId,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            task.AssigneeUserId,
            task.DueDate,
            task.CreatedAtUtc,
            task.UpdatedAtUtc);
}
