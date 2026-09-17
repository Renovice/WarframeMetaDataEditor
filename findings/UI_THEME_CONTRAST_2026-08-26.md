# Metadata Editor UI contrast repair — 2026-08-26

## Hypotheses and results

1. **Setting only `ComboBox.Foreground` and `ComboBoxItem.Foreground` is sufficient for a dark-theme selector. — FALSE.**
   The Windows/WPF default `ComboBox` template retained a light collapsed-selection surface, so light text remained nearly invisible even though popup item colors were overridden.

2. **A complete application-level dark `ComboBox` template fixes both the collapsed selector and popup list. — TRUE.**
   `App.xaml` now supplies a dark control chrome, light selection text, an explicit arrow, focus feedback, a dark popup, and readable hover/selected states. `ModIdentityWindow` and `ModSlotLayoutWindow` consume the shared styles rather than maintaining divergent color rules.

3. **Dark panel backgrounds automatically theme embedded scrollbars and grid headers. — FALSE.**
   WPF's native `ScrollBar` and `DataGridColumnHeader` templates kept bright system surfaces inside the dark editor. Application-level vertical and horizontal scrollbar templates now use a dark track, blue-grey thumb, hover feedback, and blue drag feedback. Grid headers and cells use the same panel/border palette.

4. **One color for every action is the clearest toolbar. — FALSE.**
   It is visually consistent but removes useful action recognition. The toolbar now uses a restrained semantic palette: blue for cache/general actions, teal for file utilities, orange for gameplay/status tools, gold for equipment/scaling, purple for server/coordination tools, green for saving, and neutral slate for reset. Every color still shares the same geometry, typography, and contrast rules.

5. **A flat 42-category selector is readable enough. — FALSE.**
   Categories now live in hierarchical, icon-labelled submenus: Weapons & attacks; Characters & vehicles; Upgrades & progression; Status & elements; Railjack & space; Missions & keys; Items & crafting; Cosmetics & decorations; and Other metadata. The field-section selector also shows icons for behavior, DOT, radial, stacks, fire modes, upgrades, equipment, scripting, and general fields.

6. **Styling `MenuItem.Background` removes the native white selection surface. — FALSE.**
   The platform flyout template still consumed system selection brushes and leaked a bright strip. The category selector is now a dark `ToggleButton` plus bounded `Popup` and hierarchical `TreeView`, with explicit active/inactive selection brushes. It retains hierarchy and keyboard selection without the native flyout chrome.

7. **One editable text cell is an adequate metadata UI. — FALSE.**
   The shared value editor now renders known enums as dropdowns, numbers as stepped controls, proven booleans as two-state toggles, and only genuinely free-form values as text. Fields are shown in collapsible sections and carry inline descriptions plus hover details.

8. **A `TreeView.ItemContainerStyle` automatically styles manually-created nested `TreeViewItem` controls. — FALSE.**
   The category popup built its group and category nodes as explicit containers. WPF therefore did not generate the nested containers and did not apply the parent TreeView's container style to them; child labels used the native black foreground on the dark popup. Every generated parent and child now receives the dark style and an explicit readable foreground. The current category's group opens automatically, and clicking anywhere on a group row expands/collapses it while clicks on the native arrow are guarded against double toggling.

9. **A dismiss-on-any-click popup is compatible with an interactive category tree. — FALSE.**
   With `StaysOpen=False`, group-expansion clicks could be interpreted as outside/dismiss actions before a child category was selected. The popup now stays open for tree navigation and closes explicitly only after a real child category is selected or the category toggle is pressed.

10. **A narrow permanent right sidebar is the best use of editor width. — FALSE.**
    It compressed both the category explanation and the editable field grid. The category pane and selector are now 390 pixels wide, while Field Details, Actions, and Patch Controls form a bottom strip. This gives long category descriptions room to wrap and returns the former sidebar width to the metadata table.

11. **Keeping only item display strings in the `ListBox` is a safe selection model. — FALSE.**
    The old handler cleared the active view and then searched the catalog backward by display name. A transient or duplicate display string could leave a visibly selected row with `Pick an item` and no fields. Each row now carries its exact `CatalogItem`; selection and batch targeting use the object's exact metadata path.

12. **`ItemsControl.ContainerFromElement(rootTree, clickSource)` returns the nearest nested category container. — FALSE.**
    For a click inside a child category, asking the root `TreeView` returned its direct child: the parent group. The parent's preview handler therefore mistook every subcategory click for a group-row click, collapsed the group, marked the event handled, and prevented child selection. The handler now walks from the original visual source to the nearest `TreeViewItem`; only a genuine parent-row click toggles the group, while nested rows retain their normal selection event.

13. **Giving a dialog-local `ComboBox` dark foreground/background setters keeps the shared dark template. — FALSE.**
    An implicit local style replaces the application implicit style unless it explicitly uses `BasedOn`. The weapon-damage dialog therefore bypassed `DarkComboBox`: its collapsed selector became white and its selected popup item used the Windows light-blue theme. Every dialog selector now derives from `DarkComboBox` and `DarkComboBoxItem`. Text fields and dialog buttons likewise derive from shared navy/slate semantic styles, and the older gray mod/server dialogs now use the same navy panel and border palette as the main editor.

14. **System selection brushes alone guarantee dark selected grid cells. — FALSE.**
    Some WPF themes apply selection through the cell template. The shared `DataGridCell` style now has an explicit `IsSelected` trigger in addition to application-level active and inactive selection brushes. This removes white selected rows across the main field grid, guided editor, weapon editor, and server metadata grid.

## Verification

- Release build: zero warnings and zero errors using the repository-pinned .NET 9 SDK.
- Core metadata self-test: 69/69 passed after contextual damage-filter and selection-safety coverage was added.
- Routed-event regression test: nested category click remained unhandled for normal child selection; a genuine parent-row click alone toggled the group.
- Runtime style test of the real `WeaponDamageWindow`: the profile selector resolves the shared `Chrome` template to `#101B25`, the amount field resolves the shared dark text style, and absolute damage rows render normally.
- Runtime status-dialog and category-tree harnesses remain green after the shared-resource conversion.
- UI regression harnesses are isolated under `work/ui-tests`; they are not part of the shipped editor.

## Rule for future controls

For WPF controls whose platform template owns its own chrome (`ComboBox`, and potentially other selectors), do not rely on inherited foreground/background colors alone. Use a complete shared template and visually verify both the closed and expanded states.
