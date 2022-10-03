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
        static string mappedPath = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data");
        static string svFile = Path.Combine(mappedPath, "vision-test-362406-eac69ae6377a.json");
        // GET api/values
        [Route("image")]
        [HttpPost]
        public dynamic ReadImage()
        {
            var httpRequest = HttpContext.Current.Request;
            foreach (string file in httpRequest.Files)
            {
                var postedFile = httpRequest.Files[file];
                Image image = Image.FromStream(postedFile.InputStream);
                ImageAnnotatorClient client = new ImageAnnotatorClientBuilder()
                {
                    CredentialsPath = svFile
                }.Build();
                IReadOnlyList<EntityAnnotation> textAnnotations = client.DetectText(image);
                TextAnnotation docText = client.DetectDocumentText(image);
                return new ResponseJson() { DetectText = textAnnotations, DetectDocumentText = docText };
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
                string mimeType = MimeMapping.GetMimeMapping(fileName);
                gcsStorage.UploadObject(bucketName, path, null, postedFile.InputStream);
                // for vision
                ImageAnnotatorClient client = new ImageAnnotatorClientBuilder()
                {
                    CredentialsPath = svFile
                }.Build();
                var content_byte = ByteString.FromStream(postedFile.InputStream);
                // create request
                var syncRequest = new AsyncAnnotateFileRequest
                {
                    InputConfig = new InputConfig
                    {
                        GcsSource = new GcsSource() { Uri = $"gs://synergy-vision-test-bucket/{path}" },
                        // Content = content_byte,
                        // Supported mime_types are: 'application/pdf' and 'image/tiff'
                        MimeType = mimeType
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
                var response = operation.PollUntilCompleted();
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

    internal class ResponseJson
    {
        public ResponseJson()
        {
        }

        public IReadOnlyList<EntityAnnotation> DetectText { get; set; }
        public TextAnnotation DetectDocumentText { get; set; }
    }
}   
