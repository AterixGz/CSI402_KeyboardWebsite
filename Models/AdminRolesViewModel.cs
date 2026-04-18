using System.Collections.Generic;

namespace KeyboardWebsiteProject.Models
{
    public class AdminRolesViewModel
    {
        public List<AdminRoleItem> Roles { get; set; } = new();
        public List<AdminUserItem> AdminUsers { get; set; } = new();
        public List<AdminPermissionItem> Permissions { get; set; } = new();
    }

    public class AdminRoleItem
    {
        public int RoleId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int UserCount { get; set; }
        public int PermissionCount { get; set; }
    }

    public class AdminUserItem
    {
        public int UserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public string LastActive { get; set; } = string.Empty;
        public string JoinedAt { get; set; } = string.Empty;
        public string TagClass { get; set; } = string.Empty;
        public string AvatarClass { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public bool IsOnlyAdmin { get; set; }
    }

    public class AdminPermissionItem
    {
        public int PermissionId { get; set; }
        public string PermissionName { get; set; } = string.Empty;
        public string MenuCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<int> RoleIds { get; set; } = new();
    }
}
