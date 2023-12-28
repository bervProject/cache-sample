using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Redis.OM;
using Redis.OM.Modeling;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle

var connectionString = builder.Configuration["MONGODB_URI"];
if (!string.IsNullOrWhiteSpace(connectionString))
{
    var client = new MongoClient(connectionString);
    builder.Services.AddSingleton(client);
    var _noteCollection = client.GetDatabase("test").GetCollection<Note>("note");
    var index = Builders<Note>.IndexKeys
        .Ascending(note => note.Identity);
    _noteCollection.Indexes
        .CreateOne(new CreateIndexModel<Note>(index));
}
var redisConnectionString = builder.Configuration["REDIS_URI"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    var provider = new RedisConnectionProvider(redisConnectionString);
    builder.Services.AddSingleton(provider);
    provider.Connection.CreateIndex(typeof(Note));
}
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.MapGet("/note/{id}", (Guid id, [FromServices] MongoClient mongoClient) =>
{
    var collection = mongoClient.GetDatabase("test").GetCollection<Note>("note");
    var filter = Builders<Note>.Filter.Eq(data => data.Identity, id);
    return collection.Find<Note>(filter).FirstOrDefault();
})
.WithName("GetNoteById")
.WithOpenApi();

app.MapGet("/cache/note/{id}", (Guid id, [FromServices] RedisConnectionProvider redisConnectionProvider) =>
{
    var collection = redisConnectionProvider.RedisCollection<Note>();
    return collection.FirstOrDefault(x => x.Identity == id);
})
.WithName("GetCacheNoteById")
.WithOpenApi();

app.MapPost("/generateData", ([FromServices] MongoClient mongoClient, [FromServices] RedisConnectionProvider redisConnectionProvider) =>
{
    var id = new Guid("b86847f9-4b98-417b-b09d-601eb9bf058e");
    var desc = "Any desc";
    var collection = mongoClient.GetDatabase("test").GetCollection<Note>("note");
    var filter = Builders<Note>.Filter.Eq(field => field.Identity, id);
    var update = Builders<Note>.Update.Set(field => field.Description, desc);
    var newNote = new Note
    {
        Identity = id,
        Description = desc
    };
    var existingMongo = collection.Find(filter).FirstOrDefault();
    if (existingMongo == null)
    {
        collection.InsertOne(newNote);
    }

    var collectionRedis = redisConnectionProvider.RedisCollection<Note>();
    var existing = collectionRedis.FirstOrDefault(x => x.Identity == id);
    if (existing == null)
    {
        collectionRedis.Insert(newNote);
    }
})
.WithName("GenerateData")
.WithOpenApi();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

[Document(StorageType = StorageType.Json)]
class Note()
{
    [RedisIdField]
    [BsonIgnore]
    public Ulid RedisId { get; set; }
    public ObjectId Id { get; set; }
    [Indexed]
    public Guid Identity { get; set; }
    [Indexed]
    public string Description { get; set; }
}