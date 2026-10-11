using Microsoft.EntityFrameworkCore;
using Pegaxus.Repository.Entities;

namespace Pegaxus.Repository;

public partial class AppDbContext
{
    private static void ConfigureRouteChanges(ModelBuilder builder)
    {
        builder.Entity<RoutePlan>().Property(x => x.PrimaryRoutePlanId).HasColumnName("primary_route_plan_id");
        builder.Entity<RoutePlan>().HasOne<RoutePlan>().WithMany().HasForeignKey(x => x.PrimaryRoutePlanId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<RoutePlanRoute>(segment =>
        {
            segment.Property(x => x.ServiceType).HasColumnName("service_type").HasMaxLength(100);
            segment.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(150);
            segment.Property(x => x.SupersededAt).HasColumnName("superseded_at").HasColumnType("timestamp without time zone");
        });
        builder.Entity<IncidentReport>().Property(x => x.TransportId).HasColumnName("transport_id");
        builder.Entity<IncidentReport>().Property(x => x.EmergencySettlementRequired).HasColumnName("emergency_settlement_required");
        builder.Entity<IncidentReport>().HasOne<Transport>().WithMany().HasForeignKey(x => x.TransportId).OnDelete(DeleteBehavior.Restrict);
    }
}
