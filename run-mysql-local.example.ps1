# PowerShell local MySQL configuration for the sales service.
# Do not commit real passwords.
$env:Database__Provider = 'MySql'
$env:ConnectionStrings__Sales = 'Server=127.0.0.1;Port=3306;Database=medzo_sales;User ID=medzo_sales_app;Password=replace-with-a-strong-local-password;SslMode=None;'

dotnet run --urls http://127.0.0.1:5227
