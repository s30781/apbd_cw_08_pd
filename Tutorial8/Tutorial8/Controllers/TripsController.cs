// mam nadzieje ze to bedzie ok, jak pisalem w mail nie bylem w stenie uczestniczyc w tych zajeciach,
// przesluchalem wyklad i mam nadzieje ze korzystajac z poradnikow jakie znalazlem zadanie bedzie ok

using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;

namespace Tutorial8.Controllers
{
    [ApiController]
    [Route("api")]
    public class TripsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public TripsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        
        ///DLA 1 GET /api/trips
        /// pobiera wszystkie wycieczki z bazy danych i daje info o wycieczce
        /// oraz liste krajow przypisaych do danej wycieczki
        ///
        /// GET http://localhost:5128/api/trips
        [HttpGet("trips")]
        public async Task<IActionResult> GetTrips()
        {
            var trips = new List<TripDto>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                var query = @"
                    SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                           c.Name AS CountryName
                    FROM Trip t
                    JOIN Country_Trip ct ON t.IdTrip = ct.IdTrip
                    JOIN Country c ON ct.IdCountry = c.IdCountry";

                using (var cmd = new SqlCommand(query, conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    var tripDict = new Dictionary<int, TripDto>();

                    while (await reader.ReadAsync())
                    {
                        int idTrip = reader.GetInt32(0);
                        if (!tripDict.ContainsKey(idTrip))
                        {
                            tripDict[idTrip] = new TripDto
                            {
                                IdTrip = idTrip,
                                Name = reader.GetString(1),
                                Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                DateFrom = reader.GetDateTime(3),
                                DateTo = reader.GetDateTime(4),
                                MaxPeople = reader.GetInt32(5),
                                Countries = new List<string>()
                            };
                        }

                        string country = reader.GetString(6);
                        if (!tripDict[idTrip].Countries.Contains(country))
                            tripDict[idTrip].Countries.Add(country);
                    }

                    trips = tripDict.Values.ToList();
                }
            }
            return Ok(trips);
        }
        
        
        /// DLA 2 GET /api/clients/{id}/trips
        /// pobiera wszystkie wycieczki na ktore jest zarejetrowany klient o dnym id
        /// zwraca info wycieczki oraz informacje o dacie rejestracji i zaplaty
        /// wpierw sprawdza czy kleint istnieje jak nie to 404
        /// jak klient jest a nie ma wycieczek to daje komunikat
        ///
        /// GET http://localhost:5000/api/clients/1/trips
        [HttpGet("clients/{id}/trips")]
        public async Task<IActionResult> GetClientTrips(int id)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            var trips = new List<ClientTripDto>();

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                var checkClientCmd = new SqlCommand("SELECT COUNT(*) FROM Client WHERE IdClient = @id", conn);
                checkClientCmd.Parameters.AddWithValue("@id", id);
                int clientExists = (int)await checkClientCmd.ExecuteScalarAsync();

                if (clientExists == 0)
                    return NotFound($"Client with ID {id} not found.");

                var query = @"
                    SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
                           ct.RegisteredAt, ct.PaymentDate
                    FROM Client_Trip ct
                    JOIN Trip t ON ct.IdTrip = t.IdTrip
                    WHERE ct.IdClient = @id";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            trips.Add(new ClientTripDto
                            {
                                IdTrip = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                                DateFrom = reader.GetDateTime(3),
                                DateTo = reader.GetDateTime(4),
                                MaxPeople = reader.GetInt32(5),
                                RegisteredAt = reader.GetInt32(6),
                                PaymentDate = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7)
                            });
                        }
                    }
                }
            }

            if (trips.Count == 0)
                return Ok($"Client with ID {id} has no trips.");

            return Ok(trips);
        }

        /// DLA 3 POST /api/clients
        /// robi nowego klienta bazujac na danych z json
        /// wymaga firstname lastname email
        /// wstawia do tabeli kleint i zwraca id
        ///
        /// POST http://localhost:5000/api/clients
        /// Content-Type: application/json
        /// {
        /// "firstName": "Nowy",
        /// "lastName": "Klient",
        /// "email": "nowy.klient@example.com",
        /// "telephone": "+48123456789",
        /// "pesel": "99010112345"
        /// }
        [HttpPost("clients")]
        public async Task<IActionResult> CreateClient([FromBody] ClientDto client)
        {
            if (string.IsNullOrWhiteSpace(client.FirstName) || string.IsNullOrWhiteSpace(client.LastName) ||
                string.IsNullOrWhiteSpace(client.Email))
                return BadRequest("FirstName, LastName and Email are required.");

            if (!Regex.IsMatch(client.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                return BadRequest("Invalid email format.");

            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                var query = @"
                    INSERT INTO Client (FirstName, LastName, Email, Telephone, Pesel)
                    OUTPUT INSERTED.IdClient
                    VALUES (@FirstName, @LastName, @Email, @Telephone, @Pesel)";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@FirstName", client.FirstName);
                    cmd.Parameters.AddWithValue("@LastName", client.LastName);
                    cmd.Parameters.AddWithValue("@Email", client.Email);
                    cmd.Parameters.AddWithValue("@Telephone", (object?)client.Telephone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Pesel", (object?)client.Pesel ?? DBNull.Value);

                    var newId = (int)await cmd.ExecuteScalarAsync();
                    return Created($"api/clients/{newId}", new { IdClient = newId });
                }
            }
        }
        
        /// DLA 4 PUT /api/clients/{id}/trips/{tripId}
        /// zapisuje klienta na wycieczke, sprawdza czy istnieje klient i wycieczka
        /// sprawdza czy limit osob na wycieczce oraz czy klient juz apisany
        /// dodaje wpis do Client_Trip z data obecna
        /// 
        /// PUT http://localhost:5000/api/clients/1/trips/3
        [HttpPut("clients/{id}/trips/{tripId}")]
        public async Task<IActionResult> RegisterClientToTrip(int id, int tripId)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                //sprawdzanie czy klient istenije
                var checkClient = new SqlCommand("SELECT COUNT(*) FROM Client WHERE IdClient = @id", conn);
                checkClient.Parameters.AddWithValue("@id", id);
                if ((int)await checkClient.ExecuteScalarAsync() == 0)
                    return NotFound($"Client ID {id} does not exist.");

                //sprawdzenie czy wycieczka istnieje
                var checkTrip = new SqlCommand("SELECT MaxPeople FROM Trip WHERE IdTrip = @tripId", conn);
                checkTrip.Parameters.AddWithValue("@tripId", tripId);
                object maxPeopleObj = await checkTrip.ExecuteScalarAsync();
                if (maxPeopleObj == null)
                    return NotFound($"Trip ID {tripId} does not exist.");
                int maxPeople = (int)maxPeopleObj;

                //sprawdzenie rejestracji
                var countCmd = new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdTrip = @tripId", conn);
                countCmd.Parameters.AddWithValue("@tripId", tripId);
                int currentCount = (int)await countCmd.ExecuteScalarAsync();
                if (currentCount >= maxPeople)
                    return BadRequest("Maximum number of participants reached for this trip.");

                //spradzenie czy zarjestrowany
                var checkReg = new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdClient = @id AND IdTrip = @tripId", conn);
                checkReg.Parameters.AddWithValue("@id", id);
                checkReg.Parameters.AddWithValue("@tripId", tripId);
                if ((int)await checkReg.ExecuteScalarAsync() > 0)
                    return BadRequest("Client already registered to this trip.");

                //rezygancaja z wycieczki wstaiweni
                var insertCmd = new SqlCommand(@"
                    INSERT INTO Client_Trip (IdClient, IdTrip, RegisteredAt)
                    VALUES (@id, @tripId, @regDate)", conn);
                insertCmd.Parameters.AddWithValue("@id", id);
                insertCmd.Parameters.AddWithValue("@tripId", tripId);
                insertCmd.Parameters.AddWithValue("@regDate", int.Parse(DateTime.UtcNow.ToString("yyyyMMdd")));
                await insertCmd.ExecuteNonQueryAsync();

                return Ok("Client successfully registered to the trip.");
            }
        }
        
        /// DLA 5 DELETE /api/clients/{id}/trips/{tripId}
        /// usuwa rejestracje kleinta dla wycieczki
        /// sprawda czy rejestracja istnieje
        /// jak tak to usuwa i zwraca HTTP 200 OK
        /// jak nie to zwraca HTTP 404
        /// 
        /// DELETE http://localhost:5000/api/clients/1/trips/3
        [HttpDelete("clients/{id}/trips/{tripId}")]
        public async Task<IActionResult> UnregisterClientFromTrip(int id, int tripId)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                var checkReg = new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdClient = @id AND IdTrip = @tripId", conn);
                checkReg.Parameters.AddWithValue("@id", id);
                checkReg.Parameters.AddWithValue("@tripId", tripId);

                if ((int)await checkReg.ExecuteScalarAsync() == 0)
                    return NotFound("Registration not found.");

                var deleteCmd = new SqlCommand("DELETE FROM Client_Trip WHERE IdClient = @id AND IdTrip = @tripId", conn);
                deleteCmd.Parameters.AddWithValue("@id", id);
                deleteCmd.Parameters.AddWithValue("@tripId", tripId);
                await deleteCmd.ExecuteNonQueryAsync();

                return Ok("Client successfully unregistered from the trip.");
            }
        }

        // DTO classes
        public class TripDto
        {
            public int IdTrip { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public DateTime DateFrom { get; set; }
            public DateTime DateTo { get; set; }
            public int MaxPeople { get; set; }
            public List<string> Countries { get; set; }
        }

        public class ClientTripDto
        {
            public int IdTrip { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public DateTime DateFrom { get; set; }
            public DateTime DateTo { get; set; }
            public int MaxPeople { get; set; }
            public int RegisteredAt { get; set; }
            public int? PaymentDate { get; set; }
        }

        public class ClientDto
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string Email { get; set; }
            public string? Telephone { get; set; }
            public string? Pesel { get; set; }
        }
    }
}
