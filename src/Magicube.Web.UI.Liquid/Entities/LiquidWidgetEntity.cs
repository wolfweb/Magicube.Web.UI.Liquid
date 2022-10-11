using Magicube.Core;
using Magicube.Core.Convertion;
using Magicube.Core.Models;
using Magicube.Data.Abstractions;
using Magicube.Data.Abstractions.Attributes;
using Magicube.Data.Abstractions.Mapping;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Magicube.Web.UI.Liquid.Entities {
    public class WebWidgetEntity : Entity<int> {
        [Indexed(Unique = true)]
        public string       Name     { get; set; }
        [ColumnExtend(Size = 4000)]
        public string       Content  { get; set; }
        public EntityStatus Status   { get; set; }
        public long         CreateAt { get; set; }
        public long?        UpdateAt { get; set; }
    }

    public class WebLayoutEntity : Entity<int> {
        public string       Name     { get; set; }
        public string       Remark   { get; set; }
        public long         CreateAt { get; set; }
        public long?        UpdateAt { get; set; }
        [ColumnExtend(Size = 4000)]
        public string       Content  { get; set; }
        [ColumnExtend(Size = 4000)]
        public string       Schema   { get; set; }
        public EntityStatus Status   { get; set; }
    }

    public class WebPageEntity : Entity<int> {
        public string           Name        { get; set; }
        public EntityStatus     Status      { get; set; }
        public long             CreateAt    { get; set; }
        public long?            UpdateAt    { get; set; }
        public string           Path        { get; set; }
        [ForeignColumn(Entity.IdKey)]
        public WebLayoutEntity  Content     { get; set; }
        public string           Body        { get; set; }
    }

    public class WebWidgetEntityMapping : EntityTypeConfiguration<WebWidgetEntity> {
        public override void Configure(EntityTypeBuilder<WebWidgetEntity> builder) {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Content).HasMaxLength(4000);
            builder.Property(x => x.Status).HasConversion(x => (int)x, x => (EntityStatus)x);
        }
    }

    public class WebPageEntityMapping : EntityTypeConfiguration<WebPageEntity> {
        public override void Configure(EntityTypeBuilder<WebPageEntity> builder) {
            builder.HasKey(x => x.Id);
            builder.HasOne(x => x.Content);
            builder.Property(x => x.Body).HasMaxLength(4000);
            builder.Property(x => x.Status).HasConversion(x => (int)x, x => (EntityStatus)x);
        }
    }
    public class WebLayoutEntityMapping : EntityTypeConfiguration<WebLayoutEntity> {
        public override void Configure(EntityTypeBuilder<WebLayoutEntity> builder) {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Schema).HasMaxLength(4000);
            builder.Property(x => x.Content).HasMaxLength(4000);
            builder.Property(x => x.Status).HasConversion(x => (int)x, x => (EntityStatus)x);
        }
    }
}
