using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pegaxus.API.Extensions;

namespace Pegaxus.API.Hubs;

[Authorize(Policy = JwtExtensions.TransportSpecialistPolicy)]
public sealed class ComplianceDossierHub : Hub;
