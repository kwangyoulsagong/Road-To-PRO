// 디바이스팜 플레이어에 DYLD_INSERT_LIBRARIES로 넣어 창을 만들어지는 순간부터 투명하게 한다.
// -batchmode는 카메라가 그려지지 않아 캡처가 빈 화면이 되므로 창은 그대로 두고 보이지 않게만 한다.
// 클릭은 아래 앱으로 통과시킨다. 캡처는 RenderTexture라 영향이 없다.
#import <AppKit/AppKit.h>
#import <objc/runtime.h>

typedef id (*InitFn)(id, SEL, NSRect, NSWindowStyleMask, NSBackingStoreType, BOOL);

static InitFn original;

static id hidden_init(id self, SEL cmd, NSRect rect, NSWindowStyleMask style, NSBackingStoreType backing, BOOL defer) {
    NSWindow *window = original(self, cmd, rect, style, backing, defer);
    [window setAlphaValue:0.0];
    [window setIgnoresMouseEvents:YES];
    return window;
}

__attribute__((constructor)) static void install(void) {
    Method m = class_getInstanceMethod([NSWindow class], @selector(initWithContentRect:styleMask:backing:defer:));
    original = (InitFn)method_setImplementation(m, (IMP)hidden_init);
}
