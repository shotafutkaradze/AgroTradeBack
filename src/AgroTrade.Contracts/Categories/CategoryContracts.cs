namespace AgroTrade.Contracts.Categories;

public record CategoryRequest(string Name, int? ParentId, int SortOrder, string? ImageUrl);
public record CategoryDto(int Id, int? ParentId, string Name, string Slug, int SortOrder, string? ImageUrl, int ProductCount, List<CategoryDto> Children);
