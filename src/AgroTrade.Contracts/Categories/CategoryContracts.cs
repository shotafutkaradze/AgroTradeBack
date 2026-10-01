namespace AgroTrade.Contracts.Categories;

public record CategoryRequest(string Name, int? ParentId, int SortOrder);
public record CategoryDto(int Id, int? ParentId, string Name, string Slug, int SortOrder, int ProductCount, List<CategoryDto> Children);
