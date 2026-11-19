using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;

[Activity(Label = "Arena Test Screen", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTask, ScreenOrientation = ScreenOrientation.Landscape, Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class FixtureActivity : Activity
{
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        SetContentView(new DraftView(this));
    }

    private sealed class DraftView(Activity context) : View(context)
    {
        private readonly Paint type = new(PaintFlags.AntiAlias) { Color = Color.White, TextAlign = Paint.Align.Center };
        private bool alternate;
        private long start;

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            long now = SystemClock.ElapsedRealtime();
            if (start == 0)
                start = now;
            canvas.DrawColor(Color.Rgb(27, 43, (int)(48 + (now - start) / 250 % 8)));
            type.TextSize = Width / 42f;
            canvas.DrawText(alternate ? "Fireball" : "Fiery War Axe", Width * .23f, Height * .42f, type);
            canvas.DrawText("Leeroy Jenkins", Width * .5f, Height * .42f, type);
            canvas.DrawText("King Krush", Width * .77f, Height * .42f, type);
            PostInvalidateDelayed(250);
        }

        public override bool PerformClick()
        {
            base.PerformClick();
            alternate = !alternate;
            Invalidate();
            return true;
        }

        public override bool OnTouchEvent(MotionEvent? args)
        {
            if (args?.Action == MotionEventActions.Up)
                PerformClick();
            return true;
        }
    }
}
