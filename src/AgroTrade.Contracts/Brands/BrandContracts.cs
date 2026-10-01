namespace AgroTrade.Contracts.Brands;

public record BrandRequest(string Name, int SortOrder);
public record BrandDto(int Id, string Name, string Slug, int SortOrder, int ProductCount);
