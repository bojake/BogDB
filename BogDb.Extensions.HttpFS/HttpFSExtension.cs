using BogDb.Core.Extension;
using BogDb.Core.Common;

namespace BogDb.Extensions.HttpFS
{
    public class HttpFSExtension : IExtension
    {
        public string Name => "httpfs";

        public void Load(BogDb.Core.Main.BogDatabase database)
        {
            var httpFs = new HttpFileSystem(database);
            database.RegisterFileSystem("http", httpFs);
            database.RegisterFileSystem("https", httpFs);
            database.RegisterFileSystem("s3", httpFs);
            database.RegisterFileSystem("gs", httpFs);
            database.RegisterFileSystem("gcs", httpFs);

            database.AddExtensionOption("s3_access_key_id", LogicalTypeID.STRING, "");
            database.AddExtensionOption("s3_secret_access_key", LogicalTypeID.STRING, "", isConfidential: true);
            database.AddExtensionOption("s3_session_token", LogicalTypeID.STRING, "", isConfidential: true);
            database.AddExtensionOption("s3_endpoint", LogicalTypeID.STRING, "s3.amazonaws.com");
            database.AddExtensionOption("s3_url_style", LogicalTypeID.STRING, "vhost");
            database.AddExtensionOption("s3_region", LogicalTypeID.STRING, "us-east-1");

            database.AddExtensionOption("gcs_access_key_id", LogicalTypeID.STRING, "");
            database.AddExtensionOption("gcs_secret_access_key", LogicalTypeID.STRING, "", isConfidential: true);
            database.AddExtensionOption("gcs_session_token", LogicalTypeID.STRING, "", isConfidential: true);

            database.AddExtensionOption("s3_uploader_max_num_parts_per_file", LogicalTypeID.INT64, 800000000000L);
            database.AddExtensionOption("s3_uploader_max_filesize", LogicalTypeID.INT64, 10000L);
            database.AddExtensionOption("s3_uploader_threads_limit", LogicalTypeID.INT64, 50L);
            database.AddExtensionOption("http_cache_file", LogicalTypeID.BOOL, false);
        }
    }
}
