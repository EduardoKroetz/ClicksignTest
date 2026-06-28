using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

const string clicksignAccessToken = "SEU_ACCESS_TOKEN";

#region Configure Services

builder.Services.AddValidation();

builder.Services.AddHttpClient<ClicksignClient>(client =>
{
    client.BaseAddress = new Uri("https://sandbox.clicksign.com/api/v3/");
    client.DefaultRequestHeaders.Add("Authorization", clicksignAccessToken);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
});

#endregion

var app = builder.Build();

app.UseHttpsRedirection();

app.MapPost("/signature-requests", async (SendForSignatureRequest request, ClicksignClient clicksign) =>
{
    // 1. Cria o envelope — a "caixa" que guarda documentos, signatários e status
    var envelopeId = await clicksign.PostAsync("envelopes", new CreateEnvelopeRequest(request.Name));

    // 2. Anexa o documento que será assinado (PDF em base64)
    var documentId = await clicksign.PostAsync($"envelopes/{envelopeId}/documents",
        new CreateDocumentRequest(request.Name, request.DocumentBase64));

    // 3. Adiciona o signatário - quem assina o documento
    var signerId = await clicksign.PostAsync($"envelopes/{envelopeId}/signers",
        new CreateSignerRequest(request.SignerName, request.SignerEmail));

    // 4. Requisito de autenticação — como o signatário prova identidade (token por e-mail)
    await clicksign.PostAsync($"envelopes/{envelopeId}/requirements",
        new CreateAuthRequirementRequest(documentId, signerId, auth: "email"));

    // 5. Requisito de qualificação — o que o signatário faz: assinar o documento
    await clicksign.PostAsync($"envelopes/{envelopeId}/requirements",
        new CreateQualificationRequirementRequest(documentId, signerId, role: "sign"));

    // 6. Ativa o envelope (draft -> running)
    await clicksign.PatchAsync($"envelopes/{envelopeId}",
        new ActivateEnvelopeRequest(envelopeId, status: "running"));

    // 7. Notifica o signatário por e-mail
    await clicksign.PostAsync($"envelopes/{envelopeId}/notifications",
        new EnvelopeNotificationRequest());

    return Results.Ok(new { envelopeId, documentId, signerId, status = "running" });
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

public class SendForSignatureRequest
{
    [Required]
    public required string Name { get; set; }

    [Required]
    public required string DocumentBase64 { get; set; }

    [Required()]
    public required string SignerName { get; set; }

    [Required]
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

        [JsonPropertyName("deadline_at")]
        public DateTime DeadlineAt { get; set; } = DateTime.UtcNow.AddDays(30);

        [JsonPropertyName("auto_close")]
        public bool AutoClose { get; set; } = true;
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
                // Formatação dentro do DTO para simplificar o exemplo
                Filename = FormatFileName(fileName),
                ContentBase64 = FormatBase64(contentBase64)
            }
        };
    }

    private static string FormatFileName(string fileName)
    {
        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{fileName}.pdf";
    }

    private static string FormatBase64(string contentBase64)
    {
        return contentBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? contentBase64
            : $"data:application/pdf;base64,{contentBase64}";
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
        public string Role { get; set; } // sign | party | contractor, etc
    }
}

public class EnvelopeNotificationRequest
{
    public EnvelopeNotificationRequest()
    {
        Data = new()
        {
            Attributes = new()
        };
    }


    [JsonPropertyName("data")]
    public EnvelopeNotificationData Data { get; set; }

    public class EnvelopeNotificationData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "notifications";

        [JsonPropertyName("attributes")]
        public EnvelopeNotificationAttributes Attributes { get; set; }
    }

    public class EnvelopeNotificationAttributes
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}

#endregion