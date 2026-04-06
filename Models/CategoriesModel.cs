namespace KeyboardWebsiteProject.Models;

public class CategoriesModel
{
    public List<CategoryStat> Stats { get; set; } = new();
    public List<CategoryItem> Categories { get; set; } = new();
}

public class CategoryStat
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string IconClass { get; set; } = "";
    public string IconSvg { get; set; } = "";
}

public class CategoryItem
{
    public string Name { get; set; } = "";
    public bool IsFeatured { get; set; }
    public string Status { get; set; } = "Active";
    public string Description { get; set; } = "";
    public int ProductCount { get; set; }
    public int? SubcategoryCount { get; set; }
    public bool IsExpanded { get; set; }
    public List<CategoryItem>? Children { get; set; } = new();
}
