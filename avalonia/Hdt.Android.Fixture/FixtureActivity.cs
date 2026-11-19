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
        private readonly Paint tile = new(PaintFlags.AntiAlias) { Color = Color.Rgb(9, 20, 27) };
        private bool alternate;
        private long start;

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            long now = SystemClock.ElapsedRealtime();
            if (start == 0)
                start = now;
            canvas.DrawColor(Color.Rgb(27, 43, (int)(48 + (now - start) / 250 % 8)));
            type.TextSize = Width / 45f;
            DrawOffer(canvas, alternate ? "Fireball" : "Fiery War Axe", .23f, .41f);
            DrawOffer(canvas, "Leeroy Jenkins", .5f, .42f);
            DrawOffer(canvas, "King Krush", .77f, .43f);
            PostInvalidateDelayed(250);
        }

        private void DrawOffer(Canvas canvas, string title, float x, float y)
        {
            var center = Width * x;
            var baseline = Height * y;
            var halfWidth = Width * .105f;
            canvas.DrawRoundRect(center - halfWidth, baseline - Height * .09f,
                center + halfWidth, baseline + Height * .04f, 12, 12, tile);
            canvas.DrawText(title, center, baseline, type);
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
