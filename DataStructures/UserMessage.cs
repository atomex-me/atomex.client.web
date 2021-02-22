namespace atomex_frontend.atomex_data_structures
{
  public class UserMessage
  {
    public UserMessage(int Id, string UserId, string Message, bool IsReaded)
    {
      id = Id;
      userId = UserId;
      message = Message;
      isReaded = IsReaded;
    }
    public int id { get; set; }
    public string userId { get; set; }
    public string message { get; set; }
    public bool isReaded { get; set; }
  }
}