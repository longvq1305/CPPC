using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.Infrastructure;

public sealed class BuilderDbContext(DbContextOptions<BuilderDbContext> options) : DbContext(options)
{
    public DbSet<ProblemProject> ProblemProjects => Set<ProblemProject>();
    public DbSet<GeneralInfo> GeneralInfos => Set<GeneralInfo>();
    public DbSet<Statement> Statements => Set<Statement>();
    public DbSet<StatementVersion> StatementVersions => Set<StatementVersion>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<CodeArtifact> CodeArtifacts => Set<CodeArtifact>();
    public DbSet<CodeArtifactVersion> CodeArtifactVersions => Set<CodeArtifactVersion>();
    public DbSet<TestConfiguration> TestConfigurations => Set<TestConfiguration>();
    public DbSet<Sample> Samples => Set<Sample>();
    public DbSet<SyncOperationLog> SyncOperationLogs => Set<SyncOperationLog>();
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<ModelCacheEntry> ModelCacheEntries => Set<ModelCacheEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureProject(modelBuilder);
        ConfigureStatement(modelBuilder);
        ConfigureConversation(modelBuilder);
        ConfigureCode(modelBuilder);
        ConfigureTestsAndSync(modelBuilder);
        ConfigureSettings(modelBuilder);
        ConfigureDateTimeOffsets(modelBuilder);
    }

    private static void ConfigureProject(ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<ProblemProject>();
        project.ToTable("ProblemProjects", table =>
            table.HasCheckConstraint("CK_ProblemProjects_CurrentScreen", "CurrentScreen BETWEEN 1 AND 5"));
        project.HasKey(x => x.Id);
        project.Property(x => x.InternalName).HasMaxLength(128).UseCollation("NOCASE").IsRequired();
        project.HasIndex(x => x.InternalName).IsUnique();
        project.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        project.Property(x => x.PolygonSyncPhase).HasConversion<string>().HasMaxLength(32);
        project.Property(x => x.SelectedProvider).HasConversion<string>().HasMaxLength(16);
        project.Property(x => x.SelectedModel).HasMaxLength(200);

        var generalInfo = modelBuilder.Entity<GeneralInfo>();
        generalInfo.ToTable("GeneralInfos", table =>
        {
            table.HasCheckConstraint("CK_GeneralInfos_TimeLimit", "TimeLimitMs BETWEEN 250 AND 15000 AND TimeLimitMs % 50 = 0");
            table.HasCheckConstraint("CK_GeneralInfos_MemoryLimit", "MemoryLimitMb BETWEEN 4 AND 1024");
            table.HasCheckConstraint("CK_GeneralInfos_DifferentFiles", "lower(InputFile) <> lower(OutputFile)");
        });
        generalInfo.HasKey(x => x.ProblemProjectId);
        generalInfo.Property(x => x.InputFile).HasMaxLength(64).IsRequired();
        generalInfo.Property(x => x.OutputFile).HasMaxLength(64).IsRequired();
        project.HasOne(x => x.GeneralInfo)
            .WithOne(x => x.ProblemProject)
            .HasForeignKey<GeneralInfo>(x => x.ProblemProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureStatement(ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<ProblemProject>();
        var statement = modelBuilder.Entity<Statement>();
        statement.HasKey(x => x.Id);
        statement.HasIndex(x => x.ProblemProjectId).IsUnique();
        statement.Property(x => x.Language).HasMaxLength(32).IsRequired();
        statement.Property(x => x.Title).IsRequired();
        statement.Property(x => x.Legend).IsRequired();
        statement.Property(x => x.Input).IsRequired();
        statement.Property(x => x.Output).IsRequired();
        statement.Property(x => x.Note).IsRequired();
        project.HasOne(x => x.Statement)
            .WithOne(x => x.ProblemProject)
            .HasForeignKey<Statement>(x => x.ProblemProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        var version = modelBuilder.Entity<StatementVersion>();
        version.HasKey(x => x.Id);
        version.HasIndex(x => new { x.StatementId, x.VersionNumber }).IsUnique();
        version.Property(x => x.ChangedBy).HasConversion<string>().HasMaxLength(16);
        version.Property(x => x.Provider).HasMaxLength(32);
        version.Property(x => x.Model).HasMaxLength(200);
        version.HasOne(x => x.Statement)
            .WithMany(x => x.Versions)
            .HasForeignKey(x => x.StatementId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureConversation(ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<ProblemProject>();
        var conversation = modelBuilder.Entity<Conversation>();
        conversation.HasKey(x => x.Id);
        conversation.HasIndex(x => x.ProblemProjectId).IsUnique();
        conversation.Property(x => x.RollingSummary).IsRequired();
        project.HasOne(x => x.Conversation)
            .WithOne(x => x.ProblemProject)
            .HasForeignKey<Conversation>(x => x.ProblemProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        var message = modelBuilder.Entity<ConversationMessage>();
        message.HasKey(x => x.Id);
        message.HasIndex(x => new { x.ConversationId, x.CreatedAt });
        message.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
        message.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        message.Property(x => x.Provider).HasMaxLength(32);
        message.Property(x => x.Model).HasMaxLength(200);
        message.Property(x => x.ErrorCode).HasMaxLength(80);
        message.HasOne(x => x.Conversation)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        var attachment = modelBuilder.Entity<Attachment>();
        attachment.HasKey(x => x.Id);
        attachment.HasIndex(x => x.ProblemProjectId);
        attachment.HasIndex(x => x.Sha256);
        attachment.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        attachment.Property(x => x.StoredFileName).HasMaxLength(255).IsRequired();
        attachment.Property(x => x.MimeType).HasMaxLength(128).IsRequired();
        attachment.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        attachment.Property(x => x.LocalPath).HasMaxLength(500).IsRequired();
        attachment.Property(x => x.ExtractedTextPath).HasMaxLength(500);
        attachment.Property(x => x.ProviderFileId).HasMaxLength(300);
        attachment.HasOne(x => x.ProblemProject)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.ProblemProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        attachment.HasOne(x => x.Message)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureCode(ModelBuilder modelBuilder)
    {
        var artifact = modelBuilder.Entity<CodeArtifact>();
        artifact.HasKey(x => x.Id);
        artifact.HasIndex(x => new { x.ProblemProjectId, x.Type }).IsUnique();
        artifact.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        artifact.Property(x => x.FileName).HasMaxLength(64).IsRequired();
        artifact.Property(x => x.Content).IsRequired();
        artifact.Property(x => x.LastCompileStatus).HasConversion<string>().HasMaxLength(24);
        artifact.Property(x => x.LastCompileOutput).IsRequired();
        artifact.HasOne(x => x.ProblemProject)
            .WithMany(x => x.CodeArtifacts)
            .HasForeignKey(x => x.ProblemProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        var version = modelBuilder.Entity<CodeArtifactVersion>();
        version.HasKey(x => x.Id);
        version.HasIndex(x => new { x.CodeArtifactId, x.VersionNumber }).IsUnique();
        version.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
        version.Property(x => x.Provider).HasMaxLength(32);
        version.Property(x => x.Model).HasMaxLength(200);
        version.Property(x => x.CompileStatus).HasConversion<string>().HasMaxLength(24);
        version.HasOne(x => x.CodeArtifact)
            .WithMany(x => x.Versions)
            .HasForeignKey(x => x.CodeArtifactId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureTestsAndSync(ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<ProblemProject>();
        var testConfiguration = modelBuilder.Entity<TestConfiguration>();
        testConfiguration.ToTable("TestConfigurations", table =>
        {
            table.HasCheckConstraint("CK_TestConfigurations_TestCount", "TestCount BETWEEN 1 AND 1000");
            table.HasCheckConstraint("CK_TestConfigurations_Score", "ScorePerTest >= 0");
        });
        testConfiguration.HasKey(x => x.Id);
        testConfiguration.HasIndex(x => x.ProblemProjectId).IsUnique();
        testConfiguration.Property(x => x.TestsetName).HasMaxLength(64).IsRequired();
        testConfiguration.Property(x => x.ScorePerTest).HasPrecision(12, 2);
        testConfiguration.Property(x => x.Checker).HasMaxLength(64).IsRequired();
        testConfiguration.Property(x => x.Script).IsRequired();
        testConfiguration.Property(x => x.CommitMessage).HasMaxLength(500).IsRequired();
        project.HasOne(x => x.TestConfiguration)
            .WithOne(x => x.ProblemProject)
            .HasForeignKey<TestConfiguration>(x => x.ProblemProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        var sample = modelBuilder.Entity<Sample>();
        sample.HasKey(x => x.Id);
        sample.HasIndex(x => new { x.ProblemProjectId, x.TestIndex }).IsUnique();
        sample.HasOne(x => x.ProblemProject)
            .WithMany(x => x.Samples)
            .HasForeignKey(x => x.ProblemProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        var syncLog = modelBuilder.Entity<SyncOperationLog>();
        syncLog.HasKey(x => x.Id);
        syncLog.HasIndex(x => new { x.ProblemProjectId, x.StartedAt });
        syncLog.Property(x => x.Phase).HasConversion<string>().HasMaxLength(32);
        syncLog.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        syncLog.Property(x => x.Endpoint).HasMaxLength(100).IsRequired();
        syncLog.Property(x => x.RequestFingerprint).HasMaxLength(128).IsRequired();
        syncLog.Property(x => x.ErrorCode).HasMaxLength(100);
        syncLog.HasOne(x => x.ProblemProject)
            .WithMany(x => x.SyncOperations)
            .HasForeignKey(x => x.ProblemProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSettings(ModelBuilder modelBuilder)
    {
        var setting = modelBuilder.Entity<ApplicationSetting>();
        setting.HasKey(x => x.Key);
        setting.Property(x => x.Key).HasMaxLength(100);
        setting.Property(x => x.Value).HasMaxLength(2_000).IsRequired();

        var model = modelBuilder.Entity<ModelCacheEntry>();
        model.HasKey(x => x.Id);
        model.HasIndex(x => new { x.Provider, x.ModelId }).IsUnique();
        model.Property(x => x.Provider).HasConversion<string>().HasMaxLength(16);
        model.Property(x => x.ModelId).HasMaxLength(200).IsRequired();
        model.Property(x => x.DisplayName).HasMaxLength(300).IsRequired();
        model.Property(x => x.CapabilitiesJson).IsRequired();
    }

    private static void ConfigureDateTimeOffsets(ModelBuilder modelBuilder)
    {
        var converter = new ValueConverter<DateTimeOffset, long>(
            value => value.ToUnixTimeMilliseconds(),
            value => DateTimeOffset.FromUnixTimeMilliseconds(value));
        var nullableConverter = new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : null,
            value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(converter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableConverter);
                }
            }
        }
    }
}
