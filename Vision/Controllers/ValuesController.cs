using Google.Cloud.Storage.V1;
using Google.Cloud.Vision.V1;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

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
        public dynamic ReadDocument()
        {
            AnnotateFileResponse resp = new AnnotateFileResponse();
            var httpRequest = HttpContext.Current.Request;
            foreach (string file in httpRequest.Files)
            {
                var postedFile = httpRequest.Files[file];
                // 
                var gcsStorage = StorageClient.Create();
                string bucketName = "synergy-vision-test-bucket";
                string fileName = postedFile.FileName;
                string prefixName = Path.GetFileNameWithoutExtension(fileName);
                string path = string.Format("test-input/{0}", postedFile.FileName);
                gcsStorage.UploadObject(bucketName, fileName, null, postedFile.InputStream);
                // for vision
                ImageAnnotatorClient client = ImageAnnotatorClient.Create();
                var content_byte = ByteString.FromStream(postedFile.InputStream);
                // create request
                var syncRequest = new AsyncAnnotateFileRequest
                {
                    InputConfig = new InputConfig
                    {
                        GcsSource = new GcsSource() { Uri = $"gs://synergy-vision-test-bucket/{path}" },
                        // Content = content_byte,
                        // Supported mime_types are: 'application/pdf' and 'image/tiff'
                        MimeType = "application/pdf"
                    },
                    OutputConfig = new OutputConfig
                    {
                        GcsDestination = new GcsDestination() { Uri = $"gs://synergy-vision-test-bucket/output/{prefixName}" },
                    }
                };

                syncRequest.Features.Add(new Feature
                {
                    Type = Feature.Types.Type.DocumentTextDetection
                });

                List<AsyncAnnotateFileRequest> requests =
                    new List<AsyncAnnotateFileRequest>();
                requests.Add(syncRequest);

                var operation = client.AsyncBatchAnnotateFiles(requests);
                var response = operation.PollUntilCompletedAsync();
                // download file
                var storageObjects = gcsStorage.ListObjects(bucketName);
                foreach (var storageObject in storageObjects)
                {
                    if (storageObject.Name.Contains("output/"))
                    {
                        if (storageObject.Name.Contains(prefixName))
                        {
                            using (var mem = new MemoryStream())
                            {
                                gcsStorage.DownloadObject(bucketName, storageObject.Name, mem);
                                Encoding LocalEncoding = Encoding.UTF8;
                                string settingsString = LocalEncoding.GetString(mem.ToArray());
                                //
                                var jobject = JsonConvert.DeserializeObject<JObject>(settingsString);
                                return jobject;
                            }
                        }
                        Console.WriteLine(storageObject.Name);
                    }
         
                }
                // return response;
            }
            return resp;
        }
    }
}
