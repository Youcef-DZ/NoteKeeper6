using Azure.Storage.Blobs;
using HW6NoteKeeper.Models;

namespace HW6NoteKeeper.Database
{
    /// <summary>
    /// Initializes (seeds) the database with data
    /// </summary>
    /// <remarks>Step 7</remarks>
    public class DbInitializer
    {
        /// <summary>
        /// Initializes the specified context with data
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="storageConnectionString"></param>
        public static void Initialize(DatabaseContext context, string storageConnectionString)
        {
            // Check to see if there is any data in the customer table
            if (context.Notes.Any())
            {
                // Customer table has data, nothing to do here
                return;
            }

            // Create some data
            Note[] _notes =
            {
        new Note { Summary = "Running grocery list", Details = "Milk Eggs Oranges", CreatedDateUtc = DateTime.UtcNow, ModifiedDateUtc = null},
        new Note { Summary = "Gift supplies notes", Details = "Tape & Wrapping Paper", CreatedDateUtc = DateTime.UtcNow, ModifiedDateUtc = null },
        new Note { Summary = "Valentine's Day gift ideas", Details = "Chocolate, Diamonds, New Car", CreatedDateUtc = DateTime.UtcNow, ModifiedDateUtc = null },
        new Note { Summary = "Azure tips", Details = "portal.azure.com is a quick way to get to the portal Remember double underscore for linux and colon for windows", CreatedDateUtc = DateTime.UtcNow, ModifiedDateUtc = null }
    };

            // Add the data to the in memory model
            foreach (Note _note in _notes)
            {
                context.Notes.Add(_note);
            }

            // Commit the changes to the database
            context.SaveChanges();

            // Map note Ids to corresponding files
            List<List<string>> filesList = new List<List<string>>
            {
                new List<string> { "MilkAndEggs.png", "Oranges.png" },
                new List<string> { "WrappingPaper.png", "Tape.png" },
                new List<string> { "Chocolate.png", "Diamonds.png", "NewCar.png" },
                new List<string> { "AzureLogo.png", "AzureTipsAndTricks.pdf" }
            };

            // The Customers added now are populated with their Ids
            Console.WriteLine("Notes Added:");
            int filesSet = 0;

            foreach (Note _note in context.Notes)
            {
                // Retrieve the BlobContainerClient for the note
                BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_note.Id?.ToString());

                // Create the container if it doesn't exist
                containerClient.CreateIfNotExists();

                foreach (var file in filesList[filesSet])
                {
                    // Retrieve the BlobClient for the attachment
                    BlobClient blobClient = containerClient.GetBlobClient(file);

                    // Upload the attachment
                    string filePath = $"SampleAttachments/{file}"; // Specify the path to your files
                    using (FileStream fs = File.OpenRead(filePath))
                    {
                        blobClient.Upload(fs, true);
                    }
                }

                filesSet++;

                Console.WriteLine($"\tNote Id: {_note.Id} Name: {_note.Summary}");
            }
        }

    }
}
