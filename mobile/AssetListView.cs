using Android.Content;
using Android.Views;
using Android.Widget;

namespace HoKTool.Android;

/// <summary>
/// A ListView nested inside the screen ScrollView must keep its own vertical
/// gesture. Otherwise the outer ScrollView steals ACTION_MOVE and the asset
/// browser appears frozen on touch devices.
/// </summary>
public sealed class AssetListView : ListView
{
    public AssetListView(Context context) : base(context) { }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev?.ActionMasked == MotionEventActions.Down ||
            ev?.ActionMasked == MotionEventActions.Move)
        {
            Parent?.RequestDisallowInterceptTouchEvent(true);
        }

        bool handled = base.DispatchTouchEvent(ev!);

        if (ev?.ActionMasked == MotionEventActions.Up ||
            ev?.ActionMasked == MotionEventActions.Cancel)
        {
            Parent?.RequestDisallowInterceptTouchEvent(false);
        }
        return handled;
    }
}
