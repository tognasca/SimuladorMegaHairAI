using SimuladorMegaHair.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SimuladorMegaHair.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Simulacao> Simulacoes => Set<Simulacao>();
    public DbSet<CatalogoItem> CatalogoItens => Set<CatalogoItem>();

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
        });

        // CatalogoItem
        modelBuilder.Entity<CatalogoItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Titulo).IsRequired().HasMaxLength(200);
            entity.Property(e => e.PrecoBase).HasColumnType("numeric(10,2)");
        });

        base.OnModelCreating(modelBuilder);
    }
}