using Djohnnie.SoftwareFactory.Application.Common;
using Djohnnie.SoftwareFactory.Contracts.Uploads;
using MediatR;

namespace Djohnnie.SoftwareFactory.Application.Features.Uploads;

public sealed record UploadImageCommand(UploadImageRequest Dto) : IRequest<Response<UploadedImage>>;
