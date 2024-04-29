using HW6NoteKeeper.Models;
using Microsoft.EntityFrameworkCore;

namespace HW6NoteKeeper.Database
{
    /// <summary>
    /// Coordinates Entity Framework functionality for a given data model is the database context class
    /// </summary>
    /// <seealso cref="DbContext" />
    /// <remarks>Step 6</remarks>
    public class DatabaseContext : DbContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseContext"/> class.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <remarks>Step 6a</remarks>
        public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options)
        {
            Database.EnsureCreated();
        }

        /// <summary>
        /// Represents the Note table (Entity Set)
        /// </summary>
        /// <value>
        /// The Note.
        /// </value>
        /// <remarks>Step 6b</remarks>
        public DbSet<Note> Notes { get; set; }

        /// <summary>
        /// Override this method to further configure the model that was discovered by convention from the entity types
        /// exposed in <see cref="T:Microsoft.EntityFrameworkCore.DbSet`1" /> properties on your derived context. The resulting model may be cached
        /// and re-used for subsequent instances of your derived context.
        /// </summary>
        /// <param name="modelBuilder">The builder being used to construct the model for this context. Databases (and other extensions) typically
        /// define extension methods on this object that allow you to configure aspects of the model that are specific
        /// to a given database.</param>
        /// <remarks>
        /// Step 6c
        /// If a model is explicitly set on the options for this context (via <see cref="M:Microsoft.EntityFrameworkCore.DbContextOptionsBuilder.UseModel(Microsoft.EntityFrameworkCore.Metadata.IModel)" />)
        /// then this method will not be run.
        /// </remarks>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Adds the Note to tne entity model linking it to the Customer table
            modelBuilder.Entity<Note>().ToTable("Note");
        }

        /// <summary>
        /// Configure enhanced logging
        /// </summary>
        /// <param name="optionsBuilder">The operation builder</param>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
        }
    }
}
