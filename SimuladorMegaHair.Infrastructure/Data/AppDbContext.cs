using SimuladorMegaHair.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SimuladorMegaHair.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Simulacao> Simulacoes => Set<Simulacao>();
    public DbSet<CatalogoItem> CatalogoItens => Set<CatalogoItem>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Cliente
        modelBuilder.Entity<Cliente>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nome).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Telefone).HasMaxLength(20);
        });

        // Simulacao
        modelBuilder.Entity<Simulacao>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Comprimento).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Cor).IsRequired().HasMaxLength(100);
            entity.Property(e => e.TipoCabelo).IsRequired().HasMaxLength(50);
            entity.Property(e => e.MetodoMegaHair).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ValorEstimado).HasColumnType("numeric(10,2)");

            entity.HasOne(e => e.Cliente)
                  .WithMany(c => c.Simulacoes)
                  .HasForeignKey(e => e.ClienteId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull);

            // Correção da auditoria (perf): a busca de cache em
            // SimulacoesController.Criar filtra por esses campos juntos —
            // sem índice, vira table scan conforme o histórico cresce.
            entity.HasIndex(e => new
            {
                e.FotoOriginalPath,
                e.Comprimento,
                e.Cor,
                e.TipoCabelo,
                e.MetodoMegaHair,
                e.ProviderUtilizado
            });
        });

        // CatalogoItem
        modelBuilder.Entity<CatalogoItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Titulo).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PrecoBase).HasColumnType("numeric(10,2)");
        });

        // Usuario
        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.SenhaHash).IsRequired();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}