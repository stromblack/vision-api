using Google.Cloud.Vision.V1;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http;

namespace Vision.Controllers
{
    [RoutePrefix("api/vision")]
    public class ValuesController : ApiController
    {
        // GET api/values
        [Route("image")]
        [HttpPost]
        public IEnumerable<EntityAnnotation> ReadImage()
        {
            var httpRequest = HttpContext.Current.Request;
            foreach (string file in httpRequest.Files)
            {
                var postedFile = httpRequest.Files[file];
                Image image = Image.FromStream(postedFile.InputStream);
                ImageAnnotatorClient client = ImageAnnotatorClient.Create();
                IReadOnlyList<EntityAnnotation> textAnnotations = client.DetectText(image);
                string Description = "";
                foreach (EntityAnnotation text in textAnnotations)
                {
                    Description += $"{text.Description}";
                }
                return textAnnotations;
            }
            return new List<EntityAnnotation>();
        }

        [Route("document")]
        [HttpPost]
        public BatchAnnotateFilesResponse ReadDocument()
        {
            BatchAnnotateFilesResponse resp = new BatchAnnotateFilesResponse();
            var httpRequest = HttpContext.Current.Request;
            foreach (string file in httpRequest.Files)
            {
                var postedFile = httpRequest.Files[file];
                // 
                ImageAnnotatorClient client = ImageAnnotatorClient.Create();
                var content_byte = ByteString.FromStream(postedFile.InputStream);
                // create request
                var syncRequest = new AnnotateFileRequest
                {
                    InputConfig = new InputConfig
                    {
                        Content = content_byte,
                        // Supported mime_types are: 'application/pdf' and 'image/tiff'
                        MimeType = "application/pdf"

                    }
                };

                syncRequest.Features.Add(new Feature
                {
                    Type = Feature.Types.Type.DocumentTextDetection
                });

                List<AnnotateFileRequest> requests =
                    new List<AnnotateFileRequest>();
                requests.Add(syncRequest);

                var response = client.BatchAnnotateFiles(requests);
                return response;
            }
            return resp;
        }
    }
}
