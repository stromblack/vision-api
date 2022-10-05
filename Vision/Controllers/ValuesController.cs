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
using ImageMagick;
using Google.Apis.Discovery;

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
                string fileName = postedFile.FileName;
                string prefixName = Path.GetFileNameWithoutExtension(fileName);
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

        private string SaveStreamIntoBucket(Stream stream, string fileName, string bucketName = "synergy-vision-test-bucket")
        {
            var gcsStorage = StorageClient.Create();
            string prefixName = Path.GetFileNameWithoutExtension(fileName);
            string path = string.Format("img-input/{0}", fileName);
            string mimeType = MimeMapping.GetMimeMapping(fileName);
            gcsStorage.UploadObject(bucketName, path, null, stream);
            return string.Format("gs://{0}/{1}", bucketName, path);
        }
        private JObject DownloadBucket(string prefixName, string folderTarget,string bucketName = "synergy-vision-test-bucket")
        {
            // download file
            var gcsStorage = StorageClient.Create();
            var storageObjects = gcsStorage.ListObjects(bucketName);
            foreach (var storageObject in storageObjects)
            {
                if (storageObject.Name.Contains(folderTarget))
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
                }
            }
            return new JObject(); ;
        }
        // GET api/values
        [Route("batch/image")]
        [HttpPost]
        public dynamic ReadBatchImage()
        {
            // step 1 = get file from request 
            // step 2 extract file pdf convert to n Image
            // step 3 save n Image into Bucket
            // step 4 prepare image-request for google-vision-api
            // step 5 emit batch request
            // step 6 download output-json
            var httpRequest = HttpContext.Current.Request;
            foreach (string file in httpRequest.Files)
            {
                var postedFile = httpRequest.Files[file];
                string fileName = postedFile.FileName;
                string prefixName = Path.GetFileNameWithoutExtension(fileName);
                string fileExtension = Path.GetExtension(fileName);
                //
                int page = 1;
                // list item upload;
                List<string> imageBucketList = new List<string>();
                using (MagickImageCollection images = new MagickImageCollection())
                {
                    var settings = new MagickReadSettings();
                    // Settings the density to 300 dpi will create an image with a better quality
                    settings.Density = new Density(300, 300);
                    // read
                    images.Read(postedFile.InputStream, settings);
                    foreach (var img in images)
                    {
                        img.Format = MagickFormat.Jpg;
                        img.Quality = 75;
                        string newFileName = string.Format("{0}_{1}.{2}", prefixName, page, img.Format);
                        using (var ms = new MemoryStream())
                        {
                            // write image to stream
                            img.Write(ms);
                            // save stream into bucket
                            imageBucketList.Add(SaveStreamIntoBucket(ms, newFileName));
                        }
                        page += 1;
                    }
                }
                //
                // set client
                ImageAnnotatorClient client = new ImageAnnotatorClientBuilder()
                {
                    CredentialsPath = svFile
                }.Build();
                // create request-list for n Image
                var requests = new List<AnnotateImageRequest>();
                for (int i = 0; i < imageBucketList.Count; i++)
                {
                    string fileUri = imageBucketList[i];
                    // get source image from bucket;
                    ImageSource image_source = new ImageSource() { ImageUri = fileUri };
                    Image image = new Image()
                    {
                        Source = image_source
                    };
                    var request = new AnnotateImageRequest()
                    {
                        Image = image,
                        Features = { new Feature { Type = Feature.Types.Type.DocumentTextDetection } }
                    };
                    requests.Add(request);
                }
                // 
                var requestList = new AsyncBatchAnnotateImagesRequest()
                {
                    Requests = { requests },
                    OutputConfig = new OutputConfig
                    {
                        GcsDestination = new GcsDestination() { Uri = $"gs://synergy-vision-test-bucket/output-img/{prefixName}" },
                    }
                };
                var operation = client.AsyncBatchAnnotateImages(requestList);
                var response = operation.PollUntilCompleted();
                // download bucket
                return DownloadBucket(prefixName, "output-img/");
                // return response;
            }
            return new List<string>();
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
                return DownloadBucket(prefixName, "output/");
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
