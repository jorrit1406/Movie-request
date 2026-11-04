using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RequestMovies.Configuration;

/// <summary>Configuration persistée du plugin. Seul un administrateur peut la lire ou l'écrire.</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    // --- Seerr ---
    public string SeerrUrl { get; set; } = string.Empty;

    public string SeerrApiKey { get; set; } = string.Empty;

    /// <summary>Importer automatiquement un utilisateur Jellyfin absent de Seerr au moment de sa première demande.</summary>
    public bool AutoImportUsers { get; set; } = true;

    // --- Permissions ---
    /// <summary>Suspension globale des nouvelles demandes.</summary>
    public bool RequestsSuspended { get; set; }

    /// <summary>Identifiants Jellyfin (sans tirets) des utilisateurs dont les demandes sont désactivées.</summary>
    public string[] DisabledUserIds { get; set; } = [];

    // --- Qualités (le profil et le serveur sont configurés dans Seerr ; 0 = valeur par défaut de Seerr) ---
    public string Label1080 { get; set; } = "1080p";

    public bool Enable4K { get; set; } = true;

    public string Label4K { get; set; } = "4K";

    public int ServerId1080 { get; set; }

    public int ProfileId1080 { get; set; }

    public int ServerId4K { get; set; }

    public int ProfileId4K { get; set; }

    // --- Performance ---
    public int SearchCacheSeconds { get; set; } = 60;

    public int StatusCacheSeconds { get; set; } = 15;

    public int SearchPerMinute { get; set; } = 30;

    public int RequestsPerMinute { get; set; } = 10;

    // --- Disponibilité réelle et rafraîchissement de la bibliothèque ---
    public int SyncIntervalMinutes { get; set; } = 15;

    public int MinRefreshIntervalMinutes { get; set; } = 10;

    public int MaxRefreshAttempts { get; set; } = 3;

    public int MaxWaitHours { get; set; } = 6;

    /// <summary>Secret du webhook Seerr (généré depuis la page de configuration).</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
