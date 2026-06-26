using System.Net.Http.Headers;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<ClicksignClient>(client =>
{
    client.BaseAddress = new Uri("https://sandbox.clicksign.com/api/v3/");
    client.DefaultRequestHeaders.Add("Authorization", "SEU_ACCESS_TOKEN_SANDBOX");
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
});

var app = builder.Build();

app.UseHttpsRedirection();

app.MapPost("/EnviarParaAssinatura", async (EnviarParaAssinaturaRequest input, ClicksignClient clicksign) =>
{
    var envelopeId = await clicksign.PostAsync("envelopes", new CreateEnvelopeRequest(input.Name));

    var documentId = await clicksign.PostAsync($"envelopes/{envelopeId}/documents", new CreateDocumentRequest(input.FileName, input.ContentBase64));

    var signerId = await clicksign.PostAsync($"envelopes/{envelopeId}/signers", new CreateSignerRequest(input.SignerName, input.SignerEmail));

    await clicksign.PostAsync($"envelopes/{envelopeId}/requirements", new CreateAuthRequirementRequest(documentId, signerId, auth: "email"));

    await clicksign.PostAsync($"envelopes/{envelopeId}/requirements", new CreateQualificationRequirementRequest(documentId, signerId, role: "party"));

    await clicksign.PatchAsync($"envelopes/{envelopeId}", new ActivateEnvelopeRequest(envelopeId, status: "running"));

    return Results.Ok(new
    {
        envelopeId,
        documentId,
        signerId,
        status = "running"
    });
});

app.Run();

#region Helpers

public class ClicksignClient
{
    private readonly HttpClient _httpClient;
    private static readonly MediaTypeHeaderValue JsonApi = new("application/vnd.api+json");

    public ClicksignClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> PostAsync(string url, object body)
    {
        var resp = await _httpClient.PostAsync(url, JsonContent.Create(body, JsonApi));

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");

        return (await resp.Content.ReadFromJsonAsync<ClicksignResponse>())!.Data.Id;
    }

    public async Task PatchAsync(string url, object body)
    {
        var resp = await _httpClient.PatchAsync(url, JsonContent.Create(body, JsonApi));

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"{(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
    }
}

#endregion

#region DTOs

public class EnviarParaAssinaturaRequest
{
    public required string Name { get; set; }
    public required string FileName { get; set; }
    public required string ContentBase64 { get; set; }
    public required string SignerName { get; set; }
    public required string SignerEmail { get; set; }
}

public class ClicksignResponse
{
    [JsonPropertyName("data")]
    public ClicksignData Data { get; set; }

    public class ClicksignData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }
}


public class CreateEnvelopeRequest
{
    public CreateEnvelopeRequest(string name)
    {
        Data = new()
        {
            Attributes = new()
            {
                Name = name
            }
        };
    }

    [JsonPropertyName("data")]
    public EnvelopeData Data { get; set; }

    public class EnvelopeData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "envelopes";

        [JsonPropertyName("attributes")]
        public EnvelopeAttributes Attributes { get; set; }
    }

    public class EnvelopeAttributes
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}

public class ActivateEnvelopeRequest
{
    public ActivateEnvelopeRequest(string id, string status = "running")
    {
        Data = new()
        {
            Id = id,
            Attributes = new()
            {
                Status = status
            }
        };
    }

    [JsonPropertyName("data")]
    public EnvelopeData Data { get; set; }

    public class EnvelopeData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "envelopes";

        [JsonPropertyName("attributes")]
        public EnvelopeAttributes Attributes { get; set; }
    }

    public class EnvelopeAttributes
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
    }
}

public class CreateDocumentRequest
{
    public CreateDocumentRequest(string fileName, string contentBase64)
    {
        Data = new()
        {
            Attributes = new()
            {
                Filename = fileName,
                ContentBase64 = contentBase64
            }
        };
    }

    [JsonPropertyName("data")]
    public DocumentData Data { get; set; }

    public class DocumentData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "documents";

        [JsonPropertyName("attributes")]
        public DocumentAttributes Attributes { get; set; }
    }

    public class DocumentAttributes
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("content_base64")]
        public string ContentBase64 { get; set; }
    }
}

public class CreateSignerRequest
{
    public CreateSignerRequest(string name, string email)
    {
        Data = new()
        {
            Attributes = new()
            {
                Name = name,
                Email = email
            }
        };
    }

    [JsonPropertyName("data")]
    public SignerData Data { get; set; }

    public class SignerData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "signers";

        [JsonPropertyName("attributes")]
        public SignerAttributes Attributes { get; set; }
    }

    public class SignerAttributes
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }
    }
}


public class RequirementRelationships
{
    [JsonPropertyName("document")]
    public RequirementRef Document { get; set; }

    [JsonPropertyName("signer")]
    public RequirementRef Signer { get; set; }

    public class RequirementRef
    {
        [JsonPropertyName("data")]
        public RequirementRefData Data { get; set; }

        public class RequirementRefData
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } // "documents" ou "signers"

            [JsonPropertyName("id")]
            public string Id { get; set; }
        }
    }
}

public class CreateAuthRequirementRequest
{
    public CreateAuthRequirementRequest(string documentId, string signerId, string auth = "email")
    {
        Data = new()
        {
            Attributes = new() { Auth = auth },
            Relationships = new()
            {
                Document = new() { Data = new() { Type = "documents", Id = documentId } },
                Signer = new() { Data = new() { Type = "signers", Id = signerId } }
            }
        };
    }

    [JsonPropertyName("data")]
    public RequirementData Data { get; set; }

    public class RequirementData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "requirements";

        [JsonPropertyName("attributes")]
        public RequirementAttributes Attributes { get; set; }

        [JsonPropertyName("relationships")]
        public RequirementRelationships Relationships { get; set; }
    }

    public class RequirementAttributes
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "provide_evidence";

        [JsonPropertyName("auth")]
        public string Auth { get; set; } = "email"; // email | sms | whatsapp | pix | icp_brasil | ...
    }
}

public class CreateQualificationRequirementRequest
{
    public CreateQualificationRequirementRequest(string documentId, string signerId, string role = "sign")
    {
        Data = new()
        {
            Attributes = new() { Role = role },
            Relationships = new()
            {
                Document = new() { Data = new() { Type = "documents", Id = documentId } },
                Signer = new() { Data = new() { Type = "signers", Id = signerId } }
            }
        };
    }

    [JsonPropertyName("data")]
    public RequirementData Data { get; set; }

    public class RequirementData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "requirements";

        [JsonPropertyName("attributes")]
        public RequirementAttributes Attributes { get; set; }

        [JsonPropertyName("relationships")]
        public RequirementRelationships Relationships { get; set; }
    }

    public class RequirementAttributes
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "agree";

        [JsonPropertyName("role")]
        public string Role { get; set; } // sign | party | contractor
    }
}

#endregion