using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.MediaProperties;
using Windows.AI.MachineLearning.Preview;
using Windows.UI.Core;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;
// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace SmartMirror
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        //onnx
        private const string _kModelFileName = "SqueezeNet.onnx";
        private const string _kLabelsFileName = "Labels.json";
        private ImageVariableDescriptorPreview _inputImageDescription;
        private TensorVariableDescriptorPreview _outputTensorDescription;
        private LearningModelPreview _model = null;
        private List<string> _labels = new List<string>();
        List<float> _outputVariableList = new List<float>();
        //camera
        private MediaCapture mediaCapture;
        private MediaFrameReader mediaFrameReader;
        private readonly string PHOTO_FILE_NAME = "photo.jpg";
        private readonly string VIDEO_FILE_NAME = "video.mp4";
        private bool isPreviewing;
        private SoftwareBitmap backBuffer;
        private bool taskRunning = false;
        private Thread decodingThread;


        public MainPage()
        {

            this.InitializeComponent();

            BtnStart.Click += BtnStart_Click;
            StartCapture();
        }
        VideoFrame previewFrame;
        VideoFrame videoFrame;
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            StartCapture();
        }
        async void StartCapture()
        {
            // Disable all buttons until initialization completes
            BtnStart.IsEnabled = false;

            try
            {
                if (mediaCapture != null)
                {
                    // Cleanup MediaCapture object
                    if (isPreviewing)
                    {
                        await mediaCapture.StopPreviewAsync();
                        //captureImage.Source = null;
                        //playbackElement.Source = null;
                        isPreviewing = false;
                    }

                    mediaCapture.Dispose();
                    mediaCapture = null;
                }

                StatusBlock.Text = "Initializing camera to capture audio and video...";
                // Use default initialization
                mediaCapture = new MediaCapture();
                await mediaCapture.InitializeAsync();

                // Set callbacks for failure and recording limit exceeded
                StatusBlock.Text = "Device successfully initialized for video recording!";
                mediaCapture.Failed += new MediaCaptureFailedEventHandler(mediaCapture_Failed);
                //mediaCapture.RecordLimitationExceeded += new Windows.Media.Capture.RecordLimitationExceededEventHandler(mediaCapture_RecordLimitExceeded);

                // Start Preview                
                previewElement.Source = mediaCapture;
                await mediaCapture.StartPreviewAsync();
                isPreviewing = true;
                StatusBlock.Text = "Camera preview succeeded";
                // Get information about the preview
                var previewProperties = mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;

                // Create a video frame in the desired format for the preview frame
                videoFrame = new VideoFrame(BitmapPixelFormat.Bgra8, (int)previewProperties.Width, (int)previewProperties.Height);

                Configure();
            }
            catch (Exception ex)
            {
                StatusBlock.Text = "Unable to initialize camera for audio/video mode: " + ex.Message;
            }
        }
        private async void mediaCapture_Failed(MediaCapture currentCaptureObject, MediaCaptureFailedEventArgs currentFailure)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    StatusBlock.Text = "MediaCaptureFailed: " + currentFailure.Message;


                }
                catch (Exception)
                {
                }
                finally
                {

                    StatusBlock.Text += "\nCheck if camera is diconnected. Try re-launching the app";
                }
            });
        }
        #region Cam
        private async void Cleanup()
        {
            if (mediaCapture != null)
            {
                // Cleanup MediaCapture object
                if (isPreviewing)
                {
                    await mediaCapture.StopPreviewAsync();
                    //captureImage.Source = null;
                    //playbackElement.Source = null;
                    isPreviewing = false;
                }

                mediaCapture.Dispose();
                mediaCapture = null;
            }

        }
        #endregion
        #region QR

        SoftwareBitmap currentBitmapForDecoding;

        private Object thisLock = new Object();

            System.Timers.Timer _timer; // From System.Timers


        private async void DecodeBarcode()
        {


            while (true)
            {
                previewFrame = await mediaCapture.GetPreviewFrameAsync(videoFrame);
                if (previewFrame.SoftwareBitmap != null)
                {
                    currentBitmapForDecoding = previewFrame.SoftwareBitmap;

                }
                if (currentBitmapForDecoding != null)
                {
                    DoRecognize(currentBitmapForDecoding);
                    /*
                      if (result != null)
                      {
                          await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                          {
                              ShowResult(result);
                          });
                      */
                }
                //currentBitmapForDecoding.Dispose();
                currentBitmapForDecoding = null;
                Thread.Sleep(500);
            }

        }





        async void Configure()
        {

            // Load the model
            await Task.Run(async () => await LoadModelAsync());

            _timer = new System.Timers.Timer(1000);
            _timer.Elapsed += _timer_Elapsed;
            _timer.Enabled = true; // Enable it
            _timer.Start();
            ListLog.Items.Clear();

            this.Unloaded += MainPage_Unloaded;
            //start decoding
            decodingThread = new Thread(DecodeBarcode);
            decodingThread.Start();

            
        }



        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {

        }


        void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            
        }



        // Do this when you start your application
        static int mainThreadId;

        // If called in the non main thread, will return false;
        public static bool IsMainThread
        {
            get { return System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId; }
        }
        #endregion


        /// <summary>
        /// Load the label and model files
        /// </summary>
        /// <returns></returns>
        private async Task LoadModelAsync()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusBlock.Text = $"Loading {_kModelFileName} ... patience ");

            try
            {
                // Parse labels from label file
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Assets/{_kLabelsFileName}"));
                using (var inputStream = await file.OpenReadAsync())
                using (var classicStream = inputStream.AsStreamForRead())
                using (var streamReader = new StreamReader(classicStream))
                {
                    string line = "";
                    char[] charToTrim = { '\"', ' ' };
                    while (streamReader.Peek() >= 0)
                    {
                        line = streamReader.ReadLine();
                        line.Trim(charToTrim);
                        var indexAndLabel = line.Split(':');
                        if (indexAndLabel.Count() == 2)
                        {
                            _labels.Add(indexAndLabel[1]);
                        }
                    }
                }

                // Load Model
                var modelFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Assets/{_kModelFileName}"));
                _model = await LearningModelPreview.LoadModelFromStorageFileAsync(modelFile);

                // Retrieve model input and output variable descriptions (we already know the model takes an image in and outputs a tensor)
                List<ILearningModelVariableDescriptorPreview> inputFeatures = _model.Description.InputFeatures.ToList();
                List<ILearningModelVariableDescriptorPreview> outputFeatures = _model.Description.OutputFeatures.ToList();

                _inputImageDescription =
                    inputFeatures.FirstOrDefault(feature => feature.ModelFeatureKind == LearningModelFeatureKindPreview.Image)
                    as ImageVariableDescriptorPreview;

                _outputTensorDescription =
                    outputFeatures.FirstOrDefault(feature => feature.ModelFeatureKind == LearningModelFeatureKindPreview.Tensor)
                    as TensorVariableDescriptorPreview;
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusBlock.Text = $"error: {ex.Message}");
                _model = null;
            }
        }

        /// <summary>
        /// Trigger file picker and image evaluation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void DoRecognize(SoftwareBitmap softwareBitmap)
        {
           
            try
            {
                // Load the model
                //await Task.Run(async () => await LoadModelAsync());


                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                // Display the image
                //SoftwareBitmapSource imageSource = new SoftwareBitmapSource();
                //await imageSource.SetBitmapAsync(softwareBitmap);
                //UIPreviewImage.Source = imageSource;

                // Encapsulate the image within a VideoFrame to be bound and evaluated
                VideoFrame inputImage = VideoFrame.CreateWithSoftwareBitmap(softwareBitmap);

                await Task.Run(async () =>
                {
                    // Evaluate the image
                    await EvaluateVideoFrameAsync(inputImage);
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusBlock.Text = $"error: {ex.Message}");
               
            }
        }

        /// <summary>
        /// Evaluate the VideoFrame passed in as arg
        /// </summary>
        /// <param name="inputFrame"></param>
        /// <returns></returns>
        private async Task EvaluateVideoFrameAsync(VideoFrame inputFrame)
        {
            if (inputFrame != null)
            {
                try
                {
                    // Create bindings for the input and output buffer
                    LearningModelBindingPreview binding = new LearningModelBindingPreview(_model as LearningModelPreview);
                    binding.Bind(_inputImageDescription.Name, inputFrame);
                    binding.Bind(_outputTensorDescription.Name, _outputVariableList);

                    // Process the frame with the model
                    LearningModelEvaluationResultPreview results = await _model.EvaluateAsync(binding, "test");
                    List<float> resultProbabilities = results.Outputs[_outputTensorDescription.Name] as List<float>;

                    // Find the result of the evaluation in the bound output (the top classes detected with the max confidence)
                    List<float> topProbabilities = new List<float>() { 0.0f, 0.0f, 0.0f };
                    List<int> topProbabilityLabelIndexes = new List<int>() { 0, 0, 0 };
                    for (int i = 0; i < resultProbabilities.Count(); i++)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            if (resultProbabilities[i] > topProbabilities[j])
                            {
                                topProbabilityLabelIndexes[j] = i;
                                topProbabilities[j] = resultProbabilities[i];
                                break;
                            }
                        }
                    }

                    // Display the result
                    string message = "Predominant objects detected are:";
                    for (int i = 0; i < 3; i++)
                    {
                        message += $"\n{ _labels[topProbabilityLabelIndexes[i]]} with confidence of { topProbabilities[i]}";
                    }
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusBlock.Text = message);
                }
                catch (Exception ex)
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => StatusBlock.Text = $"error: {ex.Message}");
                }

                //await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => ButtonRun.IsEnabled = true);
            }
        }
    }


}