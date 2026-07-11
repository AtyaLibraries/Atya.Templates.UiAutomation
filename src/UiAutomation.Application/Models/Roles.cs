using UiAutomation.Framework.Users;

namespace UiAutomation.Application.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  EXAMPLE — replace with your application's role vocabulary.
//
//  Roles are APPLICATION vocabulary (docs/architecture.md Section 14) and belong here,
//  not in the framework. They map the app's names onto the framework-neutral RoleKey so the
//  framework never hardcodes your product's roles.
// ─────────────────────────────────────────────────────────────────────────────
public static class Roles
{
    public static readonly RoleKey Admin = "Admin";

    public static readonly RoleKey Manager = "Manager";

    public static readonly RoleKey ReadOnly = "ReadOnly";
}
