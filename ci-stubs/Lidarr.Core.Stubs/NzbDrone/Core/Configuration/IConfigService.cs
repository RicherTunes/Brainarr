using System;

namespace NzbDrone.Core.Configuration
{
    public interface IConfigService
    {
        void SaveConfigDictionary(dynamic configValues);
        bool IsDefined(string key);
        
        // Application settings
        int Port { get; }
        int SslPort { get; }
        bool EnableSsl { get; }
        bool LaunchBrowser { get; }
        string AuthenticationMethod { get; }
        string LogLevel { get; }
        string ConsoleLogLevel { get; }
        bool LogSql { get; }
        int LogRotate { get; }
        bool FilterSentryEvents { get; }
        string Branch { get; }
        string ApiKey { get; }
        bool AnalyticsEnabled { get; }
        string LogFile { get; }
        string Theme { get; }
        bool CleanupMetadataImages { get; }

        // Media Management
        bool AutoUnmonitorPreviouslyDownloadedAlbums { get; }
        string RecycleBin { get; }
        int RecycleBinCleanupDays { get; }
        bool DownloadPropersAndRepacks { get; }
        bool CreateEmptyArtistFolders { get; }
        bool DeleteEmptyFolders { get; }
        string FileDate { get; }
        bool SkipFreeSpaceCheckWhenImporting { get; }
        int MinimumFreeSpaceWhenImporting { get; }
        bool CopyUsingHardlinks { get; }
        bool ImportExtraFiles { get; }
        string ExtraFileExtensions { get; }

        // Permissions
        bool SetPermissionsLinux { get; }
        string ChmodFolder { get; }
        string ChownGroup { get; }

        // Indexers
        int Retention { get; }
        int RssSyncInterval { get; }
        int AvailabilityDelay { get; }
        bool AllowHardcodedSubs { get; }
        string WhitelistedHardcodedSubs { get; }

        // Download Clients
        string DownloadedAlbumsFolder { get; }
        string DownloadClientWorkingFolders { get; }
        int CheckForFinishedDownloadInterval { get; }
        bool AutoRedownloadFailed { get; }
        bool RemoveFailedDownloads { get; }

        // Import Lists
        int ImportListSyncInterval { get; }
        string ListSyncLevel { get; }
        string ImportExclusions { get; }

        // Connect
        string PlexClientIdentifier { get; }

        // General
        bool UpdateAutomatically { get; }
        string UpdateMechanism { get; }
        string UpdateScriptPath { get; }
        bool ProxyEnabled { get; }
        string ProxyType { get; }
        string ProxyHostname { get; }
        int ProxyPort { get; }
        string ProxyUsername { get; }
        string ProxyPassword { get; }
        string ProxyBypassFilter { get; }
        bool ProxyBypassLocalAddresses { get; }
        string BackupFolder { get; }
        int BackupInterval { get; }
        int BackupRetention { get; }
        string CertificateValidation { get; }
        string ApplicationUrl { get; }
    }

    public interface IConfigFileProvider
    {
        string ConfigFile { get; }
        bool AuthenticationEnabled { get; }
        bool UiEnabled { get; }
        int Port { get; }
        int SslPort { get; }
        bool EnableSsl { get; }
        bool LaunchBrowser { get; }
        string InstanceName { get; }
        string Theme { get; }
        string LogLevel { get; }
        string ConsoleLogLevel { get; }
        string Branch { get; }
        string ApiKey { get; }
        string SslCertHash { get; }
        string UrlBase { get; }
        bool UpdateAutomatically { get; }
        string UpdateMechanism { get; }
        string LogFile { get; }
        string LogFolder { get; }
        int LogSizeLimit { get; }
        int LogRotate { get; }
        bool FilterSentryEvents { get; }
        bool AnalyticsEnabled { get; }
        string BindAddress { get; }
        string PostgresHost { get; }
        int PostgresPort { get; }
        string PostgresUser { get; }
        string PostgresPassword { get; }
        string PostgresMainDb { get; }
        string PostgresLogDb { get; }
        string PostgresCacheDb { get; }
        bool PostgresVersion { get; }
        string AuthenticationMethod { get; }
        string AuthenticationRequired { get; }
        string Username { get; }
        string Password { get; }
        string PasswordSalt { get; }
        string CertificateValidation { get; }
        string ApplicationUrl { get; }
    }
}